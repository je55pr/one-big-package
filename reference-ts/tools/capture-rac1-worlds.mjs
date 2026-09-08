import { existsSync } from "node:fs";
import { mkdir, readFile, stat, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { basename, join, resolve } from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));
const worldPaths = process.argv.slice(2).map((path) => resolve(path));
if (worldPaths.length === 0) throw new Error("Usage: node tools/capture-rac1-worlds.mjs <level.world.json> [...]");
for (const path of worldPaths) if (!existsSync(path)) throw new Error(`Missing capture world: ${path}`);

const captures = join(root, "captures");
await mkdir(captures, { recursive: true });
const chrome = locateChrome();
const port = Number(process.env.OBP_CAPTURE_PORT ?? 4183);
const captureTimeoutMs = Number(process.env.OBP_CAPTURE_TIMEOUT_MS ?? 180_000);
if (!Number.isFinite(captureTimeoutMs) || captureTimeoutMs < 10_000) throw new Error(`Invalid OBP_CAPTURE_TIMEOUT_MS '${process.env.OBP_CAPTURE_TIMEOUT_MS}'.`);
const server = spawn(process.execPath, [join(root, "tools", "dev-server.mjs")], {
  cwd: root,
  env: { ...process.env, PORT: String(port) },
  stdio: ["ignore", "pipe", "pipe"],
});
let serverLog = "";
server.stdout.on("data", (chunk) => { serverLog += chunk.toString(); });
server.stderr.on("data", (chunk) => { serverLog += chunk.toString(); });

try {
  await waitForServer(`http://127.0.0.1:${port}/apps/viewer/index.html`);
  console.log(`RAC1_CAPTURE_BROWSER ${chrome}`);
  console.log(`RAC1_CAPTURE_TIMEOUT_MS ${captureTimeoutMs}`);

  for (const worldPath of worldPaths) {
    const name = basename(worldPath).replace(/\.world\.json$/i, "");
    const output = join(captures, `${name}.png`);
    const metadataPath = join(captures, `${name}.capture.json`);
    const relativeWorld = worldPath.startsWith(root)
      ? `/${worldPath.slice(root.length).replaceAll("\\", "/").replace(/^\/+/, "")}`
      : null;
    if (!relativeWorld) throw new Error(`Capture world must be inside ${root}: ${worldPath}`);
    const url = `http://127.0.0.1:${port}/?capture=1&world=${encodeURIComponent(relativeWorld)}`;
    const profile = resolve(join(root, ".build", "capture-profile", name));
    await mkdir(profile, { recursive: true });

    const args = [
      "--headless=new",
      "--no-sandbox",
      "--ignore-gpu-blocklist",
      "--enable-unsafe-swiftshader",
      "--use-angle=swiftshader",
      "--disable-dev-shm-usage",
      "--hide-scrollbars",
      "--force-device-scale-factor=1",
      "--window-size=1280,720",
      "--run-all-compositor-stages-before-draw",
      "--virtual-time-budget=8000",
      `--user-data-dir=${profile}`,
      `--screenshot=${output}`,
      url,
    ];
    const started = Date.now();
    console.log(`RAC1_CAPTURE_START ${JSON.stringify({ name, worldPath, url, captureTimeoutMs })}`);
    const result = spawnSync(chrome, args, { cwd: root, encoding: "utf8", timeout: captureTimeoutMs, maxBuffer: 16 * 1024 * 1024 });
    const wallMs = Date.now() - started;
    if (result.error) {
      throw new Error([
        `Chrome capture process failed for ${name} after ${wallMs} ms: ${result.error.code ?? result.error.name}: ${result.error.message}`,
        `status=${result.status ?? "null"} signal=${result.signal ?? "null"}`,
        `stdout:\n${result.stdout ?? ""}`,
        `stderr:\n${result.stderr ?? ""}`,
      ].join("\n"));
    }
    if (result.status !== 0) {
      throw new Error(`Chrome capture failed for ${name} (${result.status}) after ${wallMs} ms:\n${result.stdout ?? ""}\n${result.stderr ?? ""}`);
    }
    const info = await stat(output);
    if (info.size < 10_000) throw new Error(`Capture ${output} is unexpectedly small (${info.size} bytes).`);
    const bytes = await readFile(output);
    const sha256 = createHash("sha256").update(bytes).digest("hex");
    const metadata = {
      world: relativeWorld,
      url,
      browser: chrome,
      width: 1280,
      height: 720,
      pngBytes: info.size,
      sha256,
      wallMs,
      captureTimeoutMs,
      captureMode: { collision: false, instances: true, fog: true, virtualTimeMs: 8000 },
      chromeStdout: (result.stdout ?? "").trim(),
      chromeStderr: (result.stderr ?? "").trim(),
    };
    await writeFile(metadataPath, JSON.stringify(metadata, null, 2) + "\n");
    console.log(`RAC1_CAPTURE ${JSON.stringify({ name, pngBytes: info.size, sha256, wallMs, output })}`);
  }
} finally {
  server.kill();
  await new Promise((resolveDone) => {
    const timer = setTimeout(resolveDone, 1500);
    server.once("exit", () => { clearTimeout(timer); resolveDone(); });
  });
  if (server.exitCode && server.exitCode !== 0) console.error(serverLog);
}

function locateChrome() {
  const candidates = [
    process.env.CHROMIUM,
    process.env.CHROME,
    "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
    "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
    "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
    "/usr/bin/google-chrome",
  ].filter(Boolean);
  for (const candidate of candidates) if (existsSync(candidate)) return candidate;
  for (const command of ["chrome", "chromium", "google-chrome", "msedge"]) {
    const probe = spawnSync(process.platform === "win32" ? "where" : "which", [command], { encoding: "utf8" });
    const found = probe.status === 0 ? probe.stdout.trim().split(/\r?\n/)[0] : "";
    if (found && existsSync(found)) return found;
  }
  throw new Error("Could not locate Chrome/Chromium/Edge. Set CHROMIUM or CHROME.");
}

async function waitForServer(url) {
  let lastError;
  for (let attempt = 0; attempt < 50; attempt++) {
    if (server.exitCode !== null) throw new Error(`Viewer server exited early (${server.exitCode}):\n${serverLog}`);
    try {
      const response = await fetch(url, { cache: "no-store" });
      if (response.ok) return;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolveWait) => setTimeout(resolveWait, 100));
  }
  throw new Error(`Viewer server did not become ready: ${lastError ?? serverLog}`);
}
