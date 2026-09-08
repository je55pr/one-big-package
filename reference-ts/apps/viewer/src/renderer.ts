import type { OBPBounds, OBPGeometry, OBPWorld } from "../../../packages/core/src/index.js";
import { identity, lookAt, multiply, perspective, translation, trs } from "./math.js";
import type { Mat4 } from "./math.js";

interface CameraState {
  yaw: number;
  pitch: number;
  distance: number;
  target: [number, number, number];
}

const DEFAULT_CAMERA: CameraState = { yaw: 0.55, pitch: 0.48, distance: 34, target: [0, 1.5, 0] };

/**
 * The level settings store fog near/far as fixed-point. 1024 (= one world unit,
 * the scale every OBP position decode uses) is the working interpretation — the
 * raw values are kept in `world.environment`, so this is a one-line retune if
 * direct evidence ever pins it to 256 instead.
 */
const FOG_DISTANCE_SCALE = 1024;

const clamp01 = (n: number): number => Math.max(0, Math.min(1, n));

interface RenderOptions {
  showCollision: boolean;
  showInstances: boolean;
  showFog: boolean;
}

interface MeshGpu {
  vao: WebGLVertexArrayObject;
  indexCount: number;
  color: readonly [number, number, number, number];
  texture: WebGLTexture | null;
}

interface ModelInstanceGpu {
  meshes: readonly MeshGpu[];
  modelMatrix: Mat4;
}

export class DebugRenderer {
  readonly gl: WebGL2RenderingContext;
  readonly camera: CameraState = { ...DEFAULT_CAMERA, target: [...DEFAULT_CAMERA.target] };
  private readonly program: WebGLProgram;
  private readonly uMvp: WebGLUniformLocation;
  private readonly uModel: WebGLUniformLocation;
  private readonly uColor: WebGLUniformLocation;
  private readonly uHasTexture: WebGLUniformLocation;
  private readonly u: (name: string) => WebGLUniformLocation | null;
  private readonly whiteTexture: WebGLTexture;
  private surfaceMeshes: MeshGpu[] = [];
  private skyMeshes: MeshGpu[] = [];
  private collisionMeshes: MeshGpu[] = [];
  /** Cross markers for instances with no resolved reusable model. */
  private instanceMeshes: MeshGpu[] = [];
  /** Owned reusable model mesh VAOs; each is uploaded once regardless of placement count. */
  private modelMeshes: MeshGpu[] = [];
  /** Per-placement transform plus references to shared model mesh VAOs. */
  private modelInstances: ModelInstanceGpu[] = [];
  private ownedTextures: WebGLTexture[] = [];
  /** Texture loads are asynchronous; redraw once when the current world's batch settles, not once per image. */
  private pendingTextureLoads = 0;
  private textureLoadGeneration = 0;
  private worldBounds: OBPBounds | null = null;
  private clearColor: readonly [number, number, number] = [0.055, 0.065, 0.08];
  /** Distance fog from the level's environment settings, in OBP world units. */
  private fog: { color: readonly [number, number, number]; near: number; far: number; nearVis: number; farVis: number } | null = null;
  private options: RenderOptions = { showCollision: true, showInstances: true, showFog: true };
  private dragging = false;
  private lastPointer: [number, number] = [0, 0];
  /** "orbit" = drag to rotate around a target; "fly" = pointer-lock mouselook + WASD. */
  private cameraMode: "orbit" | "fly" = "orbit";
  /** Free camera position, used in fly mode (orbit mode derives eye from target/distance). */
  private flyEye: [number, number, number] = [0, 0, 0];
  private readonly heldKeys = new Set<string>();
  /** Fly movement speed in world units/second; adjusted with the wheel while flying. */
  private flySpeed = 24;
  private flyRaf: number | null = null;
  private lastFlyFrame = 0;
  onCameraModeChange: ((mode: "orbit" | "fly") => void) | null = null;

  constructor(private readonly canvas: HTMLCanvasElement) {
    // preserveDrawingBuffer lets tooling read the canvas back with toDataURL for screenshots.
    const gl = canvas.getContext("webgl2", { antialias: true, alpha: false, preserveDrawingBuffer: true });
    if (!gl) throw new Error("WebGL2 is required for the OBP debug viewer.");
    this.gl = gl;
    this.program = createProgram(gl, VERTEX_SHADER, FRAGMENT_SHADER);
    const mvp = gl.getUniformLocation(this.program, "uMvp");
    const model = gl.getUniformLocation(this.program, "uModel");
    const color = gl.getUniformLocation(this.program, "uColor");
    const hasTexture = gl.getUniformLocation(this.program, "uHasTexture");
    const sampler = gl.getUniformLocation(this.program, "uTexture");
    if (!mvp || !model || !color || !hasTexture || !sampler) throw new Error("Failed to resolve shader uniforms.");
    this.uMvp = mvp;
    this.uModel = model;
    this.uColor = color;
    this.uHasTexture = hasTexture;
    const uniformCache = new Map<string, WebGLUniformLocation | null>();
    this.u = (name) => {
      if (!uniformCache.has(name)) uniformCache.set(name, gl.getUniformLocation(this.program, name));
      return uniformCache.get(name) ?? null;
    };
    gl.enable(gl.DEPTH_TEST);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    const white = gl.createTexture();
    if (!white) throw new Error("GPU texture allocation failed.");
    gl.bindTexture(gl.TEXTURE_2D, white);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
    this.whiteTexture = white;
    gl.useProgram(this.program);
    gl.uniform1i(sampler, 0);

    this.installControls();
  }

  setWorlds(worlds: readonly OBPWorld[]): void {
    this.disposeMeshes();
    this.textureLoadGeneration++;
    this.pendingTextureLoads = 0;
    this.worldBounds = unionBounds(worlds.map((world) => world.bounds));

    // Atmosphere: use the level's own background / fog / sky colour for the
    // clear colour so the sky dome reads against the right horizon.
    const env = worlds.find((world) => world.environment)?.environment;
    const bg = env?.backgroundColor ?? env?.fogColor ?? (env?.skyColor ? env.skyColor.slice(0, 3) as [number, number, number] : null);
    this.clearColor = bg && bg.every((c) => Number.isFinite(c)) ? [bg[0]!, bg[1]!, bg[2]!] : [0.055, 0.065, 0.08];

    // Distance fog. The level records the near/far as fixed-point (1024 = 1
    // world unit — the same scale as every position decode); the intensities
    // are visibility on 0..255 (255 near = clear).
    this.fog = null;
    const fc = env?.fogColor;
    const near = (env?.fogNearDistance ?? 0) / FOG_DISTANCE_SCALE;
    const far = (env?.fogFarDistance ?? 0) / FOG_DISTANCE_SCALE;
    if (fc && fc.every((c) => Number.isFinite(c)) && far > near) {
      this.fog = {
        color: [fc[0]!, fc[1]!, fc[2]!],
        near,
        far,
        nearVis: clamp01((env?.fogNearIntensity ?? 255) / 255),
        farVis: clamp01((env?.fogFarIntensity ?? 255) / 255),
      };
    }
    for (const world of worlds) {
      const colorByMaterial = new Map(world.materials.map((m) => [m.id, m.debugRgba ?? [0.75, 0.75, 0.75, 1] as const]));
      const textureByMaterial = new Map<string, WebGLTexture>();
      for (const material of world.materials) {
        if (material.image) textureByMaterial.set(material.id, this.loadTexture(material.image));
      }

      const uploadMesh = (mesh: { geometry: OBPGeometry; materialId?: string }): MeshGpu => {
        const color = colorByMaterial.get(mesh.materialId ?? "") ?? [0.75, 0.75, 0.75, 1];
        const texture = textureByMaterial.get(mesh.materialId ?? "") ?? null;
        return this.uploadGeometry(mesh.geometry, color, texture);
      };

      for (const mesh of world.meshes) {
        const gpu = uploadMesh(mesh);
        if (mesh.source?.assetKind === "sky") this.skyMeshes.push(gpu);
        else this.surfaceMeshes.push(gpu);
      }

      const modelGpuById = new Map<string, readonly MeshGpu[]>();
      for (const model of world.models ?? []) {
        const gpuMeshes = model.meshes.map(uploadMesh);
        this.modelMeshes.push(...gpuMeshes);
        modelGpuById.set(model.id, gpuMeshes);
      }

      for (const collision of world.collisionMeshes) {
        this.collisionMeshes.push(this.uploadGeometry(collision.geometry, [0.95, 0.78, 0.28, 1], null));
      }
      for (const instance of world.instances) {
        const gpuMeshes = instance.modelId ? modelGpuById.get(instance.modelId) : undefined;
        if (gpuMeshes) {
          const p = instance.transform.position;
          const r = instance.transform.rotationEuler;
          const s = instance.transform.scale;
          this.modelInstances.push({
            meshes: gpuMeshes,
            modelMatrix: trs([p.x, p.y, p.z], [r.x, r.y, r.z], [s.x, s.y, s.z]),
          });
        } else {
          const { x, y, z } = instance.transform.position;
          this.instanceMeshes.push(this.uploadGeometry(crossGeometry(x, y, z, 0.6), [0.95, 0.25, 0.65, 1], null));
        }
      }
    }
    this.frameBounds();
    this.render();
  }

  /** Create a GL texture and populate it once the image URI decodes. */
  private loadTexture(uri: string): WebGLTexture {
    const gl = this.gl;
    const texture = gl.createTexture();
    if (!texture) throw new Error("GPU texture allocation failed.");
    this.ownedTextures.push(texture);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([180, 180, 180, 255]));

    const generation = this.textureLoadGeneration;
    this.pendingTextureLoads++;
    const image = new Image();
    image.onload = () => {
      gl.bindTexture(gl.TEXTURE_2D, texture);
      gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL, 0);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image);
      gl.generateMipmap(gl.TEXTURE_2D);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.REPEAT);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.REPEAT);
      this.finishTextureLoad(generation);
    };
    image.onerror = () => this.finishTextureLoad(generation);
    image.src = uri;
    return texture;
  }

  private finishTextureLoad(generation: number): void {
    if (generation !== this.textureLoadGeneration) return;
    this.pendingTextureLoads = Math.max(0, this.pendingTextureLoads - 1);
    if (this.pendingTextureLoads === 0) this.render();
  }

  /** Point the orbit camera at the loaded world's bounds. */
  frameBounds(): void {
    const bounds = this.worldBounds;
    if (!bounds) return;
    const cx = (bounds.min.x + bounds.max.x) / 2;
    const cy = (bounds.min.y + bounds.max.y) / 2;
    const cz = (bounds.min.z + bounds.max.z) / 2;
    const diagonal = Math.max(
      1,
      Math.hypot(bounds.max.x - bounds.min.x, bounds.max.y - bounds.min.y, bounds.max.z - bounds.min.z),
    );
    this.camera.target = [cx, cy, cz];
    this.camera.distance = diagonal * 1.3;
  }

  setOptions(options: Partial<RenderOptions>): void {
    this.options = { ...this.options, ...options };
    this.render();
  }

  resetCamera(): void {
    this.camera.yaw = DEFAULT_CAMERA.yaw;
    this.camera.pitch = DEFAULT_CAMERA.pitch;
    this.camera.distance = DEFAULT_CAMERA.distance;
    this.camera.target = [...DEFAULT_CAMERA.target];
    this.frameBounds();
    if (this.cameraMode === "fly") this.flyEye = this.cameraFrame().eye;
    this.render();
  }

  setCameraMode(mode: "orbit" | "fly"): void {
    if (mode === this.cameraMode) return;
    if (mode === "fly") {
      // Enter fly at the current orbit eye, looking the same way.
      this.flyEye = this.cameraFrame().eye;
      const diag = this.worldBounds
        ? Math.hypot(
            this.worldBounds.max.x - this.worldBounds.min.x,
            this.worldBounds.max.y - this.worldBounds.min.y,
            this.worldBounds.max.z - this.worldBounds.min.z,
          )
        : 100;
      this.flySpeed = Math.max(4, diag * 0.35);
    } else {
      // Leave fly: drop an orbit target a sensible distance ahead of the camera.
      const fwd = this.forward();
      this.camera.distance = Math.min(this.camera.distance || 34, Math.max(8, this.flySpeed));
      this.camera.target = [
        this.flyEye[0] + fwd[0] * this.camera.distance,
        this.flyEye[1] + fwd[1] * this.camera.distance,
        this.flyEye[2] + fwd[2] * this.camera.distance,
      ];
      this.stopFlyLoop();
      if (document.pointerLockElement === this.canvas) document.exitPointerLock();
    }
    this.cameraMode = mode;
    this.onCameraModeChange?.(mode);
    this.render();
  }

  /** Unit view direction implied by yaw/pitch (the way the camera looks). */
  private forward(): [number, number, number] {
    const cp = Math.cos(this.camera.pitch);
    return [-Math.sin(this.camera.yaw) * cp, -Math.sin(this.camera.pitch), -Math.cos(this.camera.yaw) * cp];
  }

  /** Resolve eye / look-at target / depth range for the active camera mode. */
  private cameraFrame(): { eye: [number, number, number]; target: [number, number, number]; near: number; far: number } {
    const worldDiag = this.worldBounds
      ? Math.hypot(
          this.worldBounds.max.x - this.worldBounds.min.x,
          this.worldBounds.max.y - this.worldBounds.min.y,
          this.worldBounds.max.z - this.worldBounds.min.z,
        )
      : 1000;
    if (this.cameraMode === "fly") {
      const f = this.forward();
      const e = this.flyEye;
      return { eye: [e[0], e[1], e[2]], target: [e[0] + f[0], e[1] + f[1], e[2] + f[2]], near: 0.05, far: Math.max(2000, worldDiag * 4) };
    }
    const cp = Math.cos(this.camera.pitch);
    const eye: [number, number, number] = [
      this.camera.target[0] + Math.sin(this.camera.yaw) * cp * this.camera.distance,
      this.camera.target[1] + Math.sin(this.camera.pitch) * this.camera.distance,
      this.camera.target[2] + Math.cos(this.camera.yaw) * cp * this.camera.distance,
    ];
    return {
      eye,
      target: [...this.camera.target],
      near: Math.max(0.02, this.camera.distance * 0.002),
      far: Math.max(500, this.camera.distance * 12),
    };
  }

  render(): void {
    const gl = this.gl;
    this.resize();
    gl.viewport(0, 0, this.canvas.width, this.canvas.height);
    gl.clearColor(this.clearColor[0], this.clearColor[1], this.clearColor[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.useProgram(this.program);

    const aspect = this.canvas.width / Math.max(1, this.canvas.height);
    const { eye, target, near, far } = this.cameraFrame();
    const projection = perspective(Math.PI / 3, aspect, near, far);
    const view = lookAt(eye, target, [0, 1, 0]);
    const viewProjection = multiply(projection, view);
    const worldIdentity = identity();

    gl.uniform3f(this.u("uCameraPos"), eye[0], eye[1], eye[2]);
    const fogOn = !!this.fog && this.options.showFog;
    if (this.fog) {
      gl.uniform3f(this.u("uFogColor"), this.fog.color[0], this.fog.color[1], this.fog.color[2]);
      gl.uniform2f(this.u("uFogRange"), this.fog.near, this.fog.far);
      gl.uniform2f(this.u("uFogVisibility"), this.fog.nearVis, this.fog.farVis);
    }

    // Sky dome: the shells are built centred on the level; re-centre them on the
    // camera and draw first without writing depth, so the sky stays an infinite
    // backdrop no matter how far the camera orbits.
    if (this.skyMeshes.length) {
      const b = this.worldBounds;
      const cx = b ? (b.min.x + b.max.x) / 2 : 0;
      const cy = b ? (b.min.y + b.max.y) / 2 : 0;
      const cz = b ? (b.min.z + b.max.z) / 2 : 0;
      const skyProjection = perspective(Math.PI / 3, aspect, 1, 1_000_000);
      const skyModel = translation(eye[0] - cx, eye[1] - cy, eye[2] - cz);
      const skyMvp = multiply(multiply(skyProjection, view), skyModel);
      gl.depthMask(false);
      gl.uniform1f(this.u("uFogEnable"), 0);
      gl.uniform1f(this.u("uUnlit"), 1);
      gl.uniform1f(this.u("uAlphaCutoff"), 0.015);
      gl.uniformMatrix4fv(this.uModel, false, skyModel);
      gl.uniformMatrix4fv(this.uMvp, false, skyMvp);
      for (const mesh of this.skyMeshes) this.draw(mesh, gl.TRIANGLES);
      gl.depthMask(true);
    }

    gl.uniform1f(this.u("uFogEnable"), fogOn ? 1 : 0);
    gl.uniform1f(this.u("uUnlit"), 0);
    gl.uniform1f(this.u("uAlphaCutoff"), 0.25);
    gl.uniformMatrix4fv(this.uModel, false, worldIdentity);
    gl.uniformMatrix4fv(this.uMvp, false, viewProjection);

    for (const mesh of this.surfaceMeshes) this.draw(mesh, gl.TRIANGLES);
    if (this.options.showCollision) for (const mesh of this.collisionMeshes) this.draw(mesh, gl.TRIANGLES);
    if (this.options.showInstances) {
      for (const instance of this.modelInstances) {
        gl.uniformMatrix4fv(this.uModel, false, instance.modelMatrix);
        gl.uniformMatrix4fv(this.uMvp, false, multiply(viewProjection, instance.modelMatrix));
        for (const mesh of instance.meshes) this.draw(mesh, gl.TRIANGLES);
      }
      gl.uniformMatrix4fv(this.uModel, false, worldIdentity);
      gl.uniformMatrix4fv(this.uMvp, false, viewProjection);
      for (const mesh of this.instanceMeshes) this.draw(mesh, gl.LINES);
    }
  }

  private draw(mesh: MeshGpu, mode: number): void {
    const gl = this.gl;
    gl.bindVertexArray(mesh.vao);
    gl.uniform4fv(this.uColor, mesh.color);
    gl.uniform1f(this.uHasTexture, mesh.texture ? 1 : 0);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, mesh.texture ?? this.whiteTexture);
    gl.drawElements(mode, mesh.indexCount, gl.UNSIGNED_INT, 0);
  }

  private uploadGeometry(
    geometry: OBPGeometry,
    color: readonly [number, number, number, number],
    texture: WebGLTexture | null,
  ): MeshGpu {
    const gl = this.gl;
    const vao = gl.createVertexArray();
    const vertexBuffer = gl.createBuffer();
    const indexBuffer = gl.createBuffer();
    if (!vao || !vertexBuffer || !indexBuffer) throw new Error("GPU buffer allocation failed.");
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, vertexBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(geometry.positions), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);

    if (geometry.colors && geometry.colors.length === geometry.positions.length) {
      const colorBuffer = gl.createBuffer();
      if (!colorBuffer) throw new Error("GPU buffer allocation failed.");
      gl.bindBuffer(gl.ARRAY_BUFFER, colorBuffer);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(geometry.colors), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(1);
      gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(1);
      gl.vertexAttrib3f(1, 1, 1, 1);
    }

    if (geometry.uvs && geometry.uvs.length === (geometry.positions.length / 3) * 2) {
      const uvBuffer = gl.createBuffer();
      if (!uvBuffer) throw new Error("GPU buffer allocation failed.");
      gl.bindBuffer(gl.ARRAY_BUFFER, uvBuffer);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(geometry.uvs), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(2);
      gl.vertexAttribPointer(2, 2, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(2);
      gl.vertexAttrib2f(2, 0, 0);
    }

    if (geometry.alpha && geometry.alpha.length === geometry.positions.length / 3) {
      const alphaBuffer = gl.createBuffer();
      if (!alphaBuffer) throw new Error("GPU buffer allocation failed.");
      gl.bindBuffer(gl.ARRAY_BUFFER, alphaBuffer);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(geometry.alpha), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(3);
      gl.vertexAttribPointer(3, 1, gl.FLOAT, false, 0, 0);
    } else {
      gl.disableVertexAttribArray(3);
      gl.vertexAttrib1f(3, 1);
    }

    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint32Array(geometry.indices), gl.STATIC_DRAW);
    return { vao, indexCount: geometry.indices.length, color, texture };
  }

  private resize(): void {
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.max(1, Math.floor(this.canvas.clientWidth * ratio));
    const height = Math.max(1, Math.floor(this.canvas.clientHeight * ratio));
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
    }
  }

  private installControls(): void {
    const LOOK_SENSITIVITY = 0.0022;
    const PITCH_LIMIT = 1.35;

    this.canvas.addEventListener("pointerdown", (event) => {
      if (this.cameraMode === "fly") {
        if (document.pointerLockElement !== this.canvas) void this.canvas.requestPointerLock();
        return;
      }
      this.dragging = true;
      this.lastPointer = [event.clientX, event.clientY];
      this.canvas.setPointerCapture(event.pointerId);
    });
    this.canvas.addEventListener("pointerup", () => { this.dragging = false; });
    this.canvas.addEventListener("pointermove", (event) => {
      if (this.cameraMode === "fly") {
        if (document.pointerLockElement !== this.canvas) return;
        this.camera.yaw -= event.movementX * LOOK_SENSITIVITY;
        this.camera.pitch = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, this.camera.pitch - event.movementY * LOOK_SENSITIVITY));
        if (this.flyRaf === null) this.render();
        return;
      }
      if (!this.dragging) return;
      const dx = event.clientX - this.lastPointer[0];
      const dy = event.clientY - this.lastPointer[1];
      this.lastPointer = [event.clientX, event.clientY];
      this.camera.yaw -= dx * 0.008;
      this.camera.pitch = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, this.camera.pitch + dy * 0.008));
      this.render();
    });
    this.canvas.addEventListener("wheel", (event) => {
      event.preventDefault();
      if (this.cameraMode === "fly") {
        this.flySpeed = Math.max(0.5, Math.min(100_000, this.flySpeed * Math.exp(-event.deltaY * 0.0015)));
        return;
      }
      this.camera.distance = Math.max(0.5, Math.min(200_000, this.camera.distance * Math.exp(event.deltaY * 0.001)));
      this.render();
    }, { passive: false });
    window.addEventListener("resize", () => this.render());

    // --- fly-mode keyboard + pointer lock ---
    const FLY_CODES = new Set(["KeyW", "KeyA", "KeyS", "KeyD", "KeyQ", "KeyE", "Space", "ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight"]);
    window.addEventListener("keydown", (event) => {
      if (this.cameraMode !== "fly" || !FLY_CODES.has(event.code)) return;
      this.heldKeys.add(event.code);
      if (event.code === "Space") event.preventDefault();
    });
    window.addEventListener("keyup", (event) => { this.heldKeys.delete(event.code); });
    document.addEventListener("pointerlockchange", () => {
      if (document.pointerLockElement === this.canvas) {
        this.startFlyLoop();
      } else {
        this.heldKeys.clear();
        this.stopFlyLoop();
        this.render();
      }
    });
  }

  private startFlyLoop(): void {
    if (this.flyRaf !== null) return;
    this.lastFlyFrame = performance.now();
    const tick = (now: number): void => {
      const dt = Math.min(0.1, (now - this.lastFlyFrame) / 1000);
      this.lastFlyFrame = now;
      this.flyStep(dt);
      this.render();
      this.flyRaf = requestAnimationFrame(tick);
    };
    this.flyRaf = requestAnimationFrame(tick);
  }

  private stopFlyLoop(): void {
    if (this.flyRaf !== null) { cancelAnimationFrame(this.flyRaf); this.flyRaf = null; }
  }

  /** Integrate one fly-mode movement frame from the held keys. */
  private flyStep(dt: number): void {
    const k = this.heldKeys;
    const fwd = this.forward();
    // right = normalise(cross(forward, worldUp)), worldUp = +Y
    let rx = -fwd[2], rz = fwd[0];
    const rlen = Math.hypot(rx, rz) || 1;
    rx /= rlen; rz /= rlen;
    const boost = k.has("ShiftLeft") || k.has("ShiftRight") ? 4 : 1;
    const step = this.flySpeed * boost * dt;
    let mx = 0, my = 0, mz = 0;
    if (k.has("KeyW")) { mx += fwd[0]; my += fwd[1]; mz += fwd[2]; }
    if (k.has("KeyS")) { mx -= fwd[0]; my -= fwd[1]; mz -= fwd[2]; }
    if (k.has("KeyD")) { mx += rx; mz += rz; }
    if (k.has("KeyA")) { mx -= rx; mz -= rz; }
    if (k.has("KeyE") || k.has("Space")) my += 1;
    if (k.has("KeyQ") || k.has("ControlLeft") || k.has("ControlRight")) my -= 1;
    const mlen = Math.hypot(mx, my, mz);
    if (mlen > 1e-6) {
      this.flyEye[0] += (mx / mlen) * step;
      this.flyEye[1] += (my / mlen) * step;
      this.flyEye[2] += (mz / mlen) * step;
    }
  }

  private disposeMeshes(): void {
    this.textureLoadGeneration++;
    this.pendingTextureLoads = 0;
    for (const mesh of [...this.surfaceMeshes, ...this.skyMeshes, ...this.collisionMeshes, ...this.instanceMeshes, ...this.modelMeshes]) {
      this.gl.deleteVertexArray(mesh.vao);
    }
    for (const texture of this.ownedTextures) this.gl.deleteTexture(texture);
    this.surfaceMeshes = [];
    this.skyMeshes = [];
    this.collisionMeshes = [];
    this.instanceMeshes = [];
    this.modelMeshes = [];
    this.modelInstances = [];
    this.ownedTextures = [];
  }
}

function unionBounds(all: readonly OBPBounds[]): OBPBounds | null {
  if (all.length === 0) return null;
  const min = { x: Infinity, y: Infinity, z: Infinity };
  const max = { x: -Infinity, y: -Infinity, z: -Infinity };
  for (const b of all) {
    min.x = Math.min(min.x, b.min.x); min.y = Math.min(min.y, b.min.y); min.z = Math.min(min.z, b.min.z);
    max.x = Math.max(max.x, b.max.x); max.y = Math.max(max.y, b.max.y); max.z = Math.max(max.z, b.max.z);
  }
  return { min, max };
}

function crossGeometry(x: number, y: number, z: number, r: number): OBPGeometry {
  return {
    positions: [x - r, y, z, x + r, y, z, x, y - r, z, x, y + r, z, x, y, z - r, x, y, z + r],
    indices: [0, 1, 2, 3, 4, 5],
  };
}

function createProgram(gl: WebGL2RenderingContext, vertexSource: string, fragmentSource: string): WebGLProgram {
  const compile = (type: number, source: string): WebGLShader => {
    const shader = gl.createShader(type);
    if (!shader) throw new Error("Shader allocation failed.");
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader) ?? "Shader compile failed");
    return shader;
  };
  const program = gl.createProgram();
  if (!program) throw new Error("Program allocation failed.");
  gl.attachShader(program, compile(gl.VERTEX_SHADER, vertexSource));
  gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragmentSource));
  gl.linkProgram(program);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program) ?? "Program link failed");
  return program;
}

const VERTEX_SHADER = `#version 300 es
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aColor;
layout(location = 2) in vec2 aUv;
layout(location = 3) in float aAlpha;
uniform mat4 uMvp;
uniform mat4 uModel;
out vec3 vWorld;
out vec3 vColor;
out vec2 vUv;
out float vAlpha;
void main() {
  vWorld = (uModel * vec4(aPosition, 1.0)).xyz;
  vColor = aColor;
  vUv = aUv;
  vAlpha = aAlpha;
  gl_Position = uMvp * vec4(aPosition, 1.0);
}
`;

// Flat shading with no vertex normals: derive a per-triangle normal from screen-space
// derivatives of the world position, then a two-tone hemispheric light. aColor is
// the baked per-vertex colour (1,1,1 when the mesh has none); uTexture is the
// decoded native texture (a 1x1 white pixel when uHasTexture is 0).
const FRAGMENT_SHADER = `#version 300 es
precision highp float;
uniform vec4 uColor;
uniform float uHasTexture;
uniform sampler2D uTexture;
uniform float uAlphaCutoff;   // fragments below this are discarded
uniform float uUnlit;         // 1.0 => skip the hemispheric light (sky)
uniform vec3 uCameraPos;
uniform vec3 uFogColor;
uniform vec2 uFogRange;       // (near, far) world-unit distances
uniform vec2 uFogVisibility;  // (near, far) visibility 0..1
uniform float uFogEnable;
in vec3 vWorld;
in vec3 vColor;
in vec2 vUv;
in float vAlpha;
out vec4 outColor;
void main() {
  vec3 n = normalize(cross(dFdx(vWorld), dFdy(vWorld)));
  vec3 key = normalize(vec3(0.35, 0.85, 0.40));
  float lit = clamp(dot(n, key), 0.0, 1.0);
  float shade = mix(0.55 + 0.45 * lit, 1.0, uUnlit);
  vec4 tex = texture(uTexture, vUv);
  // The baked tfrag colours sit in a dark gamma space; lift them for the debug view.
  vec3 baked = pow(clamp(vColor, 0.0, 1.0), vec3(0.62));
  vec3 base = mix(uColor.rgb * baked, tex.rgb * (0.6 + 0.8 * baked), uHasTexture);
  float alpha = mix(uColor.a, tex.a, uHasTexture) * clamp(vAlpha, 0.0, 1.0);
  if (alpha < uAlphaCutoff) discard;
  vec3 shaded = base * shade;
  // Distance fog: visibility ramps from near to far, fog is (1 - visibility).
  float d = distance(vWorld, uCameraPos);
  float t = clamp((d - uFogRange.x) / max(uFogRange.y - uFogRange.x, 0.001), 0.0, 1.0);
  float fog = (1.0 - mix(uFogVisibility.x, uFogVisibility.y, t)) * uFogEnable;
  outColor = vec4(mix(shaded, uFogColor, fog), alpha);
}
`;
