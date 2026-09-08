import { validateWorld, worldStats, type OBPWorld } from "../../../packages/core/src/index.js";
import { rac1SyntheticWorld, rac2SyntheticWorld } from "../../../packages/fixtures/src/index.js";
import { DebugRenderer } from "./renderer.js";

const worlds = { rac1: rac1SyntheticWorld, rac2: rac2SyntheticWorld } as const;
const canvas = required<HTMLCanvasElement>("viewer");
const worldSelect = required<HTMLSelectElement>("world-select");
const collisionToggle = required<HTMLInputElement>("show-collision");
const instancesToggle = required<HTMLInputElement>("show-instances");
const fogToggle = required<HTMLInputElement>("show-fog");
const cameraMode = required<HTMLSelectElement>("camera-mode");
const flyHint = required<HTMLElement>("fly-hint");
const worldFile = required<HTMLInputElement>("world-file");
const stats = required<HTMLElement>("stats");
const storage = required<HTMLElement>("storage-status");
const renderer = new DebugRenderer(canvas);
// Debug handle for tooling / screenshots (orbit the camera from the console).
(globalThis as unknown as { __viewer?: unknown }).__viewer = renderer;

const params = new URLSearchParams(location.search);
const captureMode = params.get("capture") === "1";
if (captureMode) {
  collisionToggle.checked = false;
  instancesToggle.checked = true;
  fogToggle.checked = true;
  document.documentElement.dataset.capture = "true";
}

/** A world loaded from `?world=<url>` or the file picker; overrides the synthetic fixtures. */
let externalWorld: OBPWorld | null = null;

function selectedWorlds(): readonly OBPWorld[] {
  if (externalWorld) return [externalWorld];
  if (worldSelect.value === "rac1") return [worlds.rac1];
  if (worldSelect.value === "rac2") return [worlds.rac2];
  return [worlds.rac1, worlds.rac2];
}

async function loadExternalWorld(source: string | File): Promise<void> {
  const text = typeof source === "string" ? await (await fetch(source)).text() : await source.text();
  const world = JSON.parse(text) as OBPWorld;
  const issues = validateWorld(world);
  if (issues.length) throw new Error(`World validation failed: ${JSON.stringify(issues)}`);
  externalWorld = world;
  refresh();
}

function refresh(): void {
  const selected = selectedWorlds();
  for (const world of selected) {
    const issues = validateWorld(world);
    if (issues.length) throw new Error(`World validation failed: ${JSON.stringify(issues)}`);
  }
  renderer.setWorlds(selected);
  renderer.setOptions({ showCollision: collisionToggle.checked, showInstances: instancesToggle.checked, showFog: fogToggle.checked });
  const aggregate = selected.map(worldStats).reduce((a, b) => ({
    meshCount: a.meshCount + b.meshCount,
    renderTriangles: a.renderTriangles + b.renderTriangles,
    collisionMeshCount: a.collisionMeshCount + b.collisionMeshCount,
    collisionTriangles: a.collisionTriangles + b.collisionTriangles,
    instanceCount: a.instanceCount + b.instanceCount,
    splineCount: a.splineCount + b.splineCount,
    volumeCount: a.volumeCount + b.volumeCount,
    spawnPointCount: a.spawnPointCount + b.spawnPointCount,
  }));
  const label = externalWorld ? `${externalWorld.displayName} · ` : "";
  stats.textContent = `${label}${selected.length} world(s) · ${aggregate.renderTriangles} world-space render tris · ${aggregate.collisionTriangles} collision tris · ${aggregate.instanceCount} instance(s)`;
  document.documentElement.dataset.ready = "true";
}

async function refreshStorage(): Promise<void> {
  if (!navigator.storage?.estimate) {
    storage.textContent = "Storage API unavailable";
    return;
  }
  const estimate = await navigator.storage.estimate();
  const persisted = navigator.storage.persisted ? await navigator.storage.persisted() : false;
  storage.textContent = `Browser storage: ${formatBytes(estimate.usage ?? 0)} used / ${formatBytes(estimate.quota ?? 0)} quota · persistent ${persisted ? "yes" : "no"}`;
}

worldSelect.addEventListener("change", () => { externalWorld = null; refresh(); });
collisionToggle.addEventListener("change", () => renderer.setOptions({ showCollision: collisionToggle.checked }));
instancesToggle.addEventListener("change", () => renderer.setOptions({ showInstances: instancesToggle.checked }));
fogToggle.addEventListener("change", () => renderer.setOptions({ showFog: fogToggle.checked }));
cameraMode.addEventListener("change", () => renderer.setCameraMode(cameraMode.value === "fly" ? "fly" : "orbit"));
renderer.onCameraModeChange = (mode) => { cameraMode.value = mode; flyHint.hidden = mode !== "fly"; };
worldFile.addEventListener("change", () => {
  const file = worldFile.files?.[0];
  if (file) void loadExternalWorld(file).catch((error) => { stats.textContent = `Load failed: ${error.message}`; });
});
required<HTMLButtonElement>("reset-camera").addEventListener("click", () => renderer.resetCamera());

const worldParam = params.get("world") ?? "";
if (["rac1", "rac2", "combined"].includes(worldParam)) worldSelect.value = worldParam;
refresh();
if (worldParam && !["rac1", "rac2", "combined"].includes(worldParam)) {
  void loadExternalWorld(worldParam).catch((error) => { stats.textContent = `Load failed: ${error.message}`; });
}
void refreshStorage();

function required<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing #${id}`);
  return element as T;
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "0 B";
  const units = ["B", "KiB", "MiB", "GiB", "TiB"];
  const exponent = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${(bytes / 1024 ** exponent).toFixed(exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}
