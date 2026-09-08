export type Mat4 = Float32Array;

export function identity(): Mat4 {
  return new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
}

export function translation(x: number, y: number, z: number): Mat4 {
  return new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1]);
}

/** Compose the neutral OBP transform convention: T * Rz * Ry * Rx * S. */
export function trs(
  position: readonly [number, number, number],
  rotationEuler: readonly [number, number, number],
  scale: readonly [number, number, number],
): Mat4 {
  const [tx, ty, tz] = position;
  const [rx, ry, rz] = rotationEuler;
  const [sxScale, syScale, szScale] = scale;
  const sx = Math.sin(rx), cx = Math.cos(rx);
  const sy = Math.sin(ry), cy = Math.cos(ry);
  const sz = Math.sin(rz), cz = Math.cos(rz);

  const r00 = cz * cy;
  const r01 = cz * sy * sx - sz * cx;
  const r02 = cz * sy * cx + sz * sx;
  const r10 = sz * cy;
  const r11 = sz * sy * sx + cz * cx;
  const r12 = sz * sy * cx - cz * sx;
  const r20 = -sy;
  const r21 = cy * sx;
  const r22 = cy * cx;

  return new Float32Array([
    r00 * sxScale, r10 * sxScale, r20 * sxScale, 0,
    r01 * syScale, r11 * syScale, r21 * syScale, 0,
    r02 * szScale, r12 * szScale, r22 * szScale, 0,
    tx, ty, tz, 1,
  ]);
}

export function perspective(fovYRadians: number, aspect: number, near: number, far: number): Mat4 {
  const f = 1 / Math.tan(fovYRadians / 2);
  const nf = 1 / (near - far);
  return new Float32Array([
    f / aspect, 0, 0, 0,
    0, f, 0, 0,
    0, 0, (far + near) * nf, -1,
    0, 0, 2 * far * near * nf, 0,
  ]);
}

export function lookAt(eye: [number, number, number], center: [number, number, number], up: [number, number, number]): Mat4 {
  let [zx, zy, zz] = [eye[0] - center[0], eye[1] - center[1], eye[2] - center[2]];
  const zLen = Math.hypot(zx, zy, zz) || 1;
  zx /= zLen; zy /= zLen; zz /= zLen;

  let [xx, xy, xz] = [up[1] * zz - up[2] * zy, up[2] * zx - up[0] * zz, up[0] * zy - up[1] * zx];
  const xLen = Math.hypot(xx, xy, xz) || 1;
  xx /= xLen; xy /= xLen; xz /= xLen;
  const [yx, yy, yz] = [zy * xz - zz * xy, zz * xx - zx * xz, zx * xy - zy * xx];

  return new Float32Array([
    xx, yx, zx, 0,
    xy, yy, zy, 0,
    xz, yz, zz, 0,
    -(xx * eye[0] + xy * eye[1] + xz * eye[2]),
    -(yx * eye[0] + yy * eye[1] + yz * eye[2]),
    -(zx * eye[0] + zy * eye[1] + zz * eye[2]),
    1,
  ]);
}

export function multiply(a: Mat4, b: Mat4): Mat4 {
  const out = new Float32Array(16);
  for (let column = 0; column < 4; column++) {
    for (let row = 0; row < 4; row++) {
      let value = 0;
      for (let k = 0; k < 4; k++) value += (a[k * 4 + row] ?? 0) * (b[column * 4 + k] ?? 0);
      out[column * 4 + row] = value;
    }
  }
  return out;
}
