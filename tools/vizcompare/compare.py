#!/usr/bin/env python3
"""Local screenshot regression compare — MAE / windowed-SSIM / changed-pixel %,
plus a diff heat image. Numpy + Pillow only.

Usage:
  compare.py <baseline> <current> [--tolerances FILE] [--diffdir DIR]
  compare.py --selftest

<baseline> / <current> are either two PNG files or two directories (matched by
filename). Exit 0 if every pair is within tolerance, 1 otherwise.

Retail reference captures MUST NOT be committed or uploaded — baselines live only
on the local machine (captures/ is gitignored).
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

# Defaults: near-identical is expected (M1 saw a lone renderer-nondeterministic
# pixel on Tabora), so allow a tiny "loud pixel" budget.
DEFAULT_TOL = {"ssim": 0.995, "mae": 1.5, "over16_pct": 0.02}


def _load(p):
    return np.asarray(Image.open(p).convert("RGB"), dtype=np.float64)


def _box_mean(x, r):
    """Mean over a (2r+1) square window, via an integral image. Edges shrink."""
    h, w = x.shape
    ii = np.zeros((h + 1, w + 1), dtype=np.float64)
    ii[1:, 1:] = np.cumsum(np.cumsum(x, axis=0), axis=1)
    y0 = np.clip(np.arange(h)[:, None] - r, 0, h)
    y1 = np.clip(np.arange(h)[:, None] + r + 1, 0, h)
    x0 = np.clip(np.arange(w)[None, :] - r, 0, w)
    x1 = np.clip(np.arange(w)[None, :] + r + 1, 0, w)
    total = ii[y1, x1] - ii[y0, x1] - ii[y1, x0] + ii[y0, x0]
    count = (y1 - y0) * (x1 - x0)
    return total / count


def _ssim_channel(a, b, r=3):
    c1, c2 = (0.01 * 255) ** 2, (0.03 * 255) ** 2
    mu_a, mu_b = _box_mean(a, r), _box_mean(b, r)
    va = _box_mean(a * a, r) - mu_a * mu_a
    vb = _box_mean(b * b, r) - mu_b * mu_b
    vab = _box_mean(a * b, r) - mu_a * mu_b
    ssim = ((2 * mu_a * mu_b + c1) * (2 * vab + c2)) / (
        (mu_a**2 + mu_b**2 + c1) * (va + vb + c2)
    )
    return float(np.clip(ssim, -1.0, 1.0).mean())


def metrics(a, b):
    if a.shape != b.shape:
        return {"error": f"shape {a.shape} vs {b.shape}"}
    d = np.abs(a - b)
    per_px = d.max(axis=2)
    total = per_px.size
    return {
        "width": int(a.shape[1]),
        "height": int(a.shape[0]),
        "mae": float(d.mean()),
        "max_delta": int(per_px.max()),
        "changed_pct": float(100.0 * (per_px > 0).sum() / total),
        "over16_pct": float(100.0 * (per_px > 16).sum() / total),
        "ssim": float(np.mean([_ssim_channel(a[..., c], b[..., c]) for c in range(3)])),
    }


def diff_image(a, b, path):
    per_px = np.abs(a - b).max(axis=2)
    t = np.clip(per_px / 64.0, 0, 1)  # 64 -> full hot
    hot = np.stack([np.clip(t * 3, 0, 1),
                    np.clip(t * 3 - 1, 0, 1),
                    np.clip(t * 3 - 2, 0, 1)], axis=2)
    Image.fromarray((hot * 255).astype(np.uint8)).save(path)


def _verdict(m, tol):
    if "error" in m:
        return False, [m["error"]]
    fails = []
    if m["ssim"] < tol["ssim"]:
        fails.append(f"ssim {m['ssim']:.5f} < {tol['ssim']}")
    if m["mae"] > tol["mae"]:
        fails.append(f"mae {m['mae']:.3f} > {tol['mae']}")
    if m["over16_pct"] > tol["over16_pct"]:
        fails.append(f"over16_pct {m['over16_pct']:.4f} > {tol['over16_pct']}")
    return (not fails), fails


def _tol_for(name, table):
    t = dict(DEFAULT_TOL)
    t.update(table.get("default", {}))
    t.update(table.get(name, {}))
    return t


def _pairs(base, cur):
    base, cur = Path(base), Path(cur)
    if base.is_file() and cur.is_file():
        return [(base.stem, base, cur)]
    out = []
    for f in sorted(base.glob("*.png")):
        c = cur / f.name
        if c.exists():
            out.append((f.stem, f, c))
        else:
            out.append((f.stem, f, None))
    return out


def run(base, cur, tol_file=None, diffdir=None):
    table = json.loads(Path(tol_file).read_text()) if tol_file and Path(tol_file).exists() else {}
    diffdir = Path(diffdir) if diffdir else (Path(cur) if Path(cur).is_dir() else Path(cur).parent) / "_diff"

    rows, ok = [], True
    for name, bp, cp in _pairs(base, cur):
        if cp is None:
            print(f"  MISSING  {name}  (no current capture)")
            ok = False
            continue
        a, b = _load(bp), _load(cp)
        m = metrics(a, b)
        passed, why = _verdict(m, _tol_for(name, table))
        ok &= passed
        if not passed and "error" not in m:
            diffdir.mkdir(parents=True, exist_ok=True)
            diff_image(a, b, diffdir / f"{name}_diff.png")
        tag = "ok  " if passed else "FAIL"
        extra = "" if "error" in m else f"ssim={m['ssim']:.5f} mae={m['mae']:.3f} maxd={m['max_delta']} over16%={m['over16_pct']:.4f}"
        print(f"  {tag} {name}  {extra}" + (f"   -> {'; '.join(why)}" if why else ""))
        rows.append({"name": name, **m, "passed": passed})

    return ok, rows


def selftest():
    rng = np.random.default_rng(0)
    base = rng.integers(0, 256, (64, 96, 3)).astype(np.float64)

    same = metrics(base, base.copy())
    assert same["ssim"] > 0.9999 and same["mae"] == 0, same

    spike = base.copy()
    spike[10, 20] = 255 - spike[10, 20]
    p, _ = _verdict(metrics(base, spike), DEFAULT_TOL)
    assert p, "a single loud pixel should be within budget"

    noisy = np.clip(base + rng.normal(0, 12, base.shape), 0, 255)
    p, _ = _verdict(metrics(base, noisy), DEFAULT_TOL)
    assert not p, "global noise should fail"

    shifted = np.roll(base, 3, axis=1)
    m = metrics(base, shifted)
    assert m["ssim"] < 0.9, f"a 3px shift should drop ssim ({m['ssim']})"

    print("selftest ok")


def main(argv):
    if "--selftest" in argv:
        selftest()
        return 0
    if len(argv) < 2:
        print(__doc__)
        return 2
    base, cur = argv[0], argv[1]
    tol = argv[argv.index("--tolerances") + 1] if "--tolerances" in argv else None
    ddir = argv[argv.index("--diffdir") + 1] if "--diffdir" in argv else None
    ok, _ = run(base, cur, tol, ddir)
    print("\nPASS" if ok else "\nFAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
