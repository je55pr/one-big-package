import asyncio
import json
import os
import re
import shutil
import subprocess
import time
from pathlib import Path

from playwright.async_api import async_playwright

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "captures"
OUTPUT = OUT_DIR / "stage0-synthetic.png"
METADATA = OUT_DIR / "stage0-synthetic.json"
BUNDLE = ROOT / ".build" / "capture" / "obp-stage0-capture.js"
INDEX = ROOT / "apps" / "viewer" / "index.html"
STYLES = ROOT / "apps" / "viewer" / "src" / "styles.css"


def injected_html() -> str:
    html = INDEX.read_text(encoding="utf-8")
    html = re.sub(r'<link[^>]+href="/apps/viewer/src/styles\.css"[^>]*>', "", html)
    html = re.sub(r'<script[^>]+src="/\.build/apps/viewer/src/main\.js"[^>]*></script>', "", html)
    return html.replace("</head>", f"<style>{STYLES.read_text(encoding='utf-8')}</style></head>")


def display_is_available(display: str) -> bool:
    xdpyinfo = shutil.which("xdpyinfo")
    if xdpyinfo:
        try:
            return subprocess.run(
                [xdpyinfo, "-display", display],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=2,
                check=False,
            ).returncode == 0
        except subprocess.TimeoutExpired:
            return False

    local = re.fullmatch(r":(\d+)(?:\.\d+)?", display)
    if local:
        return Path(f"/tmp/.X11-unix/X{local.group(1)}").exists()
    return True


def ensure_display() -> subprocess.Popen | None:
    display = os.environ.get("DISPLAY")
    if display and display_is_available(display):
        return None
    if display:
        os.environ.pop("DISPLAY", None)

    xvfb = shutil.which("Xvfb")
    if not xvfb:
        return None
    for number in range(90, 120):
        if Path(f"/tmp/.X11-unix/X{number}").exists():
            continue
        proc = subprocess.Popen(
            [xvfb, f":{number}", "-screen", "0", "1280x720x24", "-nolisten", "tcp"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        os.environ["DISPLAY"] = f":{number}"
        time.sleep(0.2)
        if proc.poll() is None:
            return proc
        proc.wait()
    raise RuntimeError("Could not start Xvfb")


async def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    if not BUNDLE.exists():
        raise RuntimeError(f"Missing capture bundle: {BUNDLE}. Run npm run build:capture first.")

    xvfb = ensure_display()
    console_errors: list[str] = []
    try:
        async with async_playwright() as playwright:
            chromium = os.environ.get("CHROMIUM", "/usr/bin/chromium")
            browser = await playwright.chromium.launch(
                headless=False,
                executable_path=chromium,
                args=["--no-sandbox", "--ignore-gpu-blocklist", "--use-angle=gl"],
            )
            page = await browser.new_page(viewport={"width": 1280, "height": 720})
            page.set_default_timeout(30_000)
            page.on("console", lambda message: console_errors.append(f"{message.type}: {message.text}") if message.type == "error" else None)
            page.on("pageerror", lambda error: console_errors.append(f"pageerror: {error}"))

            # Deliberately do not navigate. This mirrors the proven RTA capture path:
            # keep Playwright's about:blank page, inject the DOM, then inject one IIFE bundle.
            await page.set_content(injected_html(), wait_until="load")
            await page.add_script_tag(content=BUNDLE.read_text(encoding="utf-8"))
            await page.wait_for_function("document.documentElement.dataset.ready === 'true'")
            await page.wait_for_timeout(100)

            diagnostic = await page.evaluate(
                """() => {
                    const canvas = document.querySelector('#viewer');
                    const gl = canvas?.getContext('webgl2');
                    const ext = gl?.getExtension('WEBGL_debug_renderer_info');
                    return {
                        href: location.href,
                        ready: document.documentElement.dataset.ready,
                        webgl2: !!gl,
                        renderer: gl && ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : null,
                        vendor: gl && ext ? gl.getParameter(ext.UNMASKED_VENDOR_WEBGL) : null,
                        stats: document.querySelector('#stats')?.textContent ?? null,
                        storage: document.querySelector('#storage-status')?.textContent ?? null,
                    };
                }"""
            )
            if diagnostic["href"] != "about:blank":
                raise RuntimeError(f"Capture unexpectedly navigated to {diagnostic['href']}")
            if not diagnostic["webgl2"]:
                raise RuntimeError("WebGL2 unavailable in injected capture page")

            await page.screenshot(path=str(OUTPUT))
            METADATA.write_text(
                json.dumps({**diagnostic, "consoleErrors": console_errors}, indent=2) + "\n",
                encoding="utf-8",
            )
            await browser.close()
            print(OUTPUT)
            print(json.dumps(diagnostic, indent=2))
    finally:
        if xvfb is not None:
            xvfb.terminate()
            try:
                xvfb.wait(timeout=2)
            except subprocess.TimeoutExpired:
                xvfb.kill()


if __name__ == "__main__":
    asyncio.run(main())
