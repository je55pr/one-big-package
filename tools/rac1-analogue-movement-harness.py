#!/usr/bin/env python3
"""Deterministic R&C1 analogue movement experiments through PCSX2 input replay.

Retail inputs and raw captures stay under captures/. The committed tool only
contains the PCSX2 movie format, PINE field map, automation and derivation code.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import math
import shutil
import socket
import struct
import subprocess
import time
from ctypes import wintypes
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CAPTURES = ROOT / "captures"
PLAYER_STATE = 0x0013F3D0
PLAYER_MOBY = 0x01845E80
READ32 = 2
NEUTRAL = 127
BUTTON_MASKS = {"cross": 0x4000}
FRAME_ADVANCE_VK = 0x76  # F7
PWSH_RUNNER = Path(r"C:\ChatGPT\Tools\pwsh-runner.cmd")
PICKER_HELPER = ROOT / "tools" / "rac1-pcsx2-input-recording-picker.ps1"
KNOWN_FIELDS = {
    "yaw": (PLAYER_STATE + 0x18, "f32"),
    "disp_x": (PLAYER_STATE + 0x80, "f32"),
    "disp_y": (PLAYER_STATE + 0x84, "f32"),
    "disp_z": (PLAYER_STATE + 0x88, "f32"),
    "control_dir_x": (PLAYER_STATE + 0x0F0, "f32"),
    "control_dir_y": (PLAYER_STATE + 0x0F4, "f32"),
    "control_dir_z": (PLAYER_STATE + 0x0F8, "f32"),
    "target_yaw": (PLAYER_STATE + 0x100, "f32"),
    "pos_x": (PLAYER_MOBY + 0x10, "f32"),
    "pos_y": (PLAYER_MOBY + 0x14, "f32"),
    "pos_z": (PLAYER_MOBY + 0x18, "f32"),
    "moby_yaw": (PLAYER_MOBY + 0x48, "f32"),
    "sequence_word": (PLAYER_MOBY + 0x50, "u32"),
}

def capture_path(path: Path) -> Path:
    target = path.resolve()
    try:
        target.relative_to(CAPTURES.resolve())
    except ValueError as exc:
        raise ValueError(f"raw artifact must be under {CAPTURES}") from exc
    return target

def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
def load_plan(path: Path) -> list[dict[str, object]]:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if raw.get("schema") != 1 or not isinstance(raw.get("segments"), list):
        raise ValueError("plan must be schema 1 with a segments array")
    result: list[dict[str, object]] = []
    for index, item in enumerate(raw["segments"]):
        frames = int(item["frames"])
        left = tuple(int(value) for value in item["left"])
        right = tuple(int(value) for value in item.get("right", [NEUTRAL, NEUTRAL]))
        buttons = tuple(str(value).lower() for value in item.get("buttons", []))
        if frames <= 0 or len(left) != 2 or len(right) != 2:
            raise ValueError(f"invalid segment {index}")
        if any(value < 0 or value > 255 for value in (*left, *right)):
            raise ValueError(f"stick byte outside 0..255 in segment {index}")
        unknown_buttons = sorted(set(buttons) - BUTTON_MASKS.keys())
        if unknown_buttons:
            raise ValueError(f"unknown buttons in segment {index}: {unknown_buttons}")
        result.append({
            "label": str(item.get("label", f"segment-{index}")),
            "frames": frames,
            "left": left,
            "right": right,
            "buttons": buttons,
        })
    return result

def expand_plan(plan: list[dict[str, object]]) -> list[dict[str, object]]:
    frames: list[dict[str, object]] = []
    for segment_index, segment in enumerate(plan):
        for local_frame in range(int(segment["frames"])):
            frames.append({**segment, "segment_index": segment_index, "local_frame": local_frame})
    return frames
def _fixed_ascii(value: str, size: int) -> bytes:
    encoded = value.encode("ascii", errors="replace")[: size - 1]
    return encoded + bytes(size - len(encoded))

def pad_bytes(
    left: tuple[int, int],
    right: tuple[int, int],
    buttons: tuple[str, ...] = (),
) -> bytes:
    # PCSX2 PadData v1: active-low DS2 button flags, RXY, LXY, then 12 pressure bytes.
    flags = 0xFFFF
    for button in buttons:
        flags &= ~BUTTON_MASKS[button]
    return struct.pack("<H", flags) + bytes((right[0], right[1], left[0], left[1])) + bytes(12)

def build_movie(plan_path: Path, savestate: Path, movie: Path) -> dict[str, object]:
    movie = capture_path(movie)
    plan = load_plan(plan_path)
    frames = expand_plan(plan)
    header = b"".join((
        bytes((1,)),
        _fixed_ascii("PCSX2-2.6.3", 50),
        _fixed_ascii("MjauBridge analogue harness", 255),
        _fixed_ascii("Ratchet & Clank", 255),
        struct.pack("<II?", len(frames), 0, True),
    ))
    neutral_port = pad_bytes((NEUTRAL, NEUTRAL), (NEUTRAL, NEUTRAL))
    body = bytearray()
    for frame in frames:
        body += pad_bytes(frame["left"], frame["right"], frame["buttons"])
        body += neutral_port
    movie.parent.mkdir(parents=True, exist_ok=True)
    movie.write_bytes(header + body)
    companion = Path(f"{movie}_SaveState.p2s")
    shutil.copy2(savestate, companion)
    manifest = {
        "schema": 1,
        "movie": movie.name,
        "movieSha256": sha256(movie),
        "stateSha256": sha256(savestate),
        "totalFrames": len(frames),
        "headerBytes": len(header),
        "frameBytes": 36,
        "segments": [
            {
                "label": segment["label"],
                "frames": segment["frames"],
                "left": list(segment["left"]),
                "right": list(segment["right"]),
                "buttons": list(segment["buttons"]),
            }
            for segment in plan
        ],
    }
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest

def set_ini_value(text: str, section: str, key: str, value: str) -> str:
    lines = text.splitlines()
    marker = f"[{section}]"
    try:
        start = lines.index(marker)
    except ValueError:
        lines.extend(["", marker, f"{key} = {value}"])
        return "\n".join(lines) + "\n"
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("[")), len(lines))
    prefix = f"{key} ="
    for i in range(start + 1, end):
        if lines[i].startswith(prefix):
            lines[i] = f"{key} = {value}"
            return "\n".join(lines) + "\n"
    lines.insert(end, f"{key} = {value}")
    return "\n".join(lines) + "\n"

def prepare_profile(source: Path, target: Path, pine_port: int) -> dict[str, object]:
    target = capture_path(target)
    ignore = shutil.ignore_patterns("cache", "logs", "sstates", "snaps", "videos")
    shutil.copytree(source, target, dirs_exist_ok=True, ignore=ignore)
    for name in ("cache", "logs", "sstates", "snaps", "videos"):
        (target / name).mkdir(exist_ok=True)
    ini = target / "inis" / "PCSX2.ini"
    text = ini.read_text(encoding="utf-8-sig")
    patches = [
        ("EmuCore", "EnablePINE", "true"),
        ("EmuCore", "PINESlot", str(pine_port)),
        ("EmuCore", "EnableRecordingTools", "true"),
        ("Hotkeys", "FrameAdvance", "Keyboard/F7"),
        ("SPU2/Output", "OutputMuted", "true"),
    ]
    for section, key, value in patches:
        text = set_ini_value(text, section, key, value)
    ini.write_text(text, encoding="utf-8")
    return {
        "profile": str(target),
        "pinePort": pine_port,
        "frameAdvance": "Keyboard/F7",
        "pcsx2": str(target / "pcsx2-qt.exe"),
    }

class Pine:
    def __init__(self, host: str, port: int, timeout: float = 2.0):
        self.sock = socket.create_connection((host, port), timeout=timeout)
        self.sock.settimeout(timeout)

    def close(self) -> None:
        self.sock.close()

    def _recv_exact(self, count: int) -> bytes:
        data = bytearray()
        while len(data) < count:
            chunk = self.sock.recv(count - len(data))
            if not chunk:
                raise ConnectionError("PINE closed")
            data.extend(chunk)
        return bytes(data)

    def read32(self, addresses: list[int]) -> list[int]:
        payload = b"".join(bytes((READ32,)) + struct.pack("<I", address) for address in addresses)
        self.sock.sendall(struct.pack("<I", len(payload) + 4) + payload)
        response_size = struct.unpack("<I", self._recv_exact(4))[0]
        body = self._recv_exact(response_size - 4)
        if not body or body[0] != 0:
            status = body[0] if body else "empty"
            raise RuntimeError(f"PINE read failed with status {status}")
        expected = 1 + 4 * len(addresses)
        if len(body) < expected:
            raise RuntimeError(f"short PINE response: {len(body)} < {expected}")
        return list(struct.unpack("<" + "I" * len(addresses), body[1:expected]))

def u32_to_f32(value: int) -> float:
    return struct.unpack("<f", struct.pack("<I", value))[0]

def sample_player(pine: Pine, scan_bytes: int = 0x180) -> dict[str, object]:
    scan_offsets = list(range(0, scan_bytes, 4))
    known_addresses = [address for address, _ in KNOWN_FIELDS.values()]
    addresses = known_addresses + [PLAYER_STATE + offset for offset in scan_offsets]
    values = pine.read32(addresses)
    sample: dict[str, object] = {}
    for (name, (_, kind)), raw in zip(KNOWN_FIELDS.items(), values[: len(known_addresses)]):
        sample[name] = u32_to_f32(raw) if kind == "f32" else raw
    sequence_word = int(sample.pop("sequence_word"))
    sample["next_sequence"] = (sequence_word >> 16) & 0xFF
    sample["sequence"] = (sequence_word >> 24) & 0xFF
    scan_values = values[len(known_addresses):]
    sample["candidate_words"] = {
        f"0x{offset:03x}": raw for offset, raw in zip(scan_offsets, scan_values)
    }
    return sample

user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
VK_MENU = 0x12
KEYEVENTF_KEYUP = 0x0002

def _windows_for_pid(pid: int) -> list[int]:
    result: list[int] = []
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    @callback_type
    def callback(hwnd, _):
        process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        if process_id.value == pid and user32.IsWindowVisible(hwnd):
            result.append(int(hwnd))
        return True
    user32.EnumWindows(callback, 0)
    return result

def _window_text(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    buffer = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buffer, len(buffer))
    return buffer.value

def _send_key(vk: int, alt: bool = False) -> None:
    if alt:
        user32.keybd_event(VK_MENU, 0, 0, 0)
    user32.keybd_event(vk, 0, 0, 0)
    user32.keybd_event(vk, 0, KEYEVENTF_KEYUP, 0)
    if alt:
        user32.keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0)

def _activate_window(window: int) -> None:
    foreground = user32.GetForegroundWindow()
    scratch = wintypes.DWORD()
    foreground_thread = user32.GetWindowThreadProcessId(foreground, ctypes.byref(scratch))
    target_thread = user32.GetWindowThreadProcessId(window, ctypes.byref(scratch))
    current_thread = kernel32.GetCurrentThreadId()
    attached: list[int] = []
    for thread_id in {foreground_thread, target_thread}:
        if thread_id and thread_id != current_thread:
            if user32.AttachThreadInput(current_thread, thread_id, True):
                attached.append(thread_id)
    try:
        user32.BringWindowToTop(window)
        user32.SetActiveWindow(window)
        user32.SetForegroundWindow(window)
    finally:
        for thread_id in attached:
            user32.AttachThreadInput(current_thread, thread_id, False)

def _foreground_key(hwnd: int, vk: int, alt: bool = False) -> None:
    _activate_window(hwnd)
    time.sleep(0.04)
    _send_key(vk, alt)

def _main_window(pid: int) -> int:
    windows = [(hwnd, _window_text(hwnd)) for hwnd in _windows_for_pid(pid)]
    if not windows:
        raise RuntimeError(f"visible PCSX2 window not found for pid {pid}")
    # Game-title-only windows are normal when PCSX2 hides its branding.
    return max(windows, key=lambda item: len(item[1]))[0]

def _wait_for_input_recording_picker(pid: int, timeout: float = 4.0) -> int:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        for hwnd in _windows_for_pid(pid):
            if (
                _window_text(hwnd) == "Select a File"
                and user32.GetDlgItem(hwnd, 1148)
                and user32.GetDlgItem(hwnd, 1)
            ):
                return hwnd
        time.sleep(0.05)
    raise TimeoutError(f"input-recording picker not found for pid {pid}")

def _invoke_picker_path(dialog: int, movie: Path) -> None:
    absolute = movie.resolve()
    if not PWSH_RUNNER.exists():
        raise FileNotFoundError(f"PowerShell runner not found: {PWSH_RUNNER}")
    completed = subprocess.run(
        [
            "cmd.exe", "/d", "/s", "/c",
            str(PWSH_RUNNER), "-File", str(PICKER_HELPER),
            str(dialog), str(absolute),
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=False,
    )
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout).strip()
        raise RuntimeError(f"input-recording picker automation failed: {detail}")

def start_movie_replay(pid: int, movie: Path) -> int:
    movie = capture_path(movie).resolve()
    main = _main_window(pid)
    # Qt accelerators only open PCSX2's replay picker. Analogue game input is
    # supplied entirely by the generated input-recording movie.
    _activate_window(main)
    time.sleep(0.08)
    _send_key(ord("T"), alt=True)
    time.sleep(0.15)
    _send_key(ord("I"))
    time.sleep(0.15)
    _send_key(ord("P"))
    dialog = _wait_for_input_recording_picker(pid)
    _invoke_picker_path(dialog, movie)
    deadline = time.monotonic() + 5.0
    while time.monotonic() < deadline:
        if dialog not in _windows_for_pid(pid):
            return main
        time.sleep(0.05)
    raise TimeoutError("input-recording picker did not close")

def frame_advance(hwnd: int, settle: float) -> None:
    _foreground_key(hwnd, FRAME_ADVANCE_VK)
    time.sleep(settle)

def capture_trial(
    pid: int,
    port: int,
    plan_path: Path,
    movie: Path,
    raw_out: Path,
    settle: float,
    reload_wait: float,
) -> dict[str, object]:
    raw_out = capture_path(raw_out)
    movie = capture_path(movie)
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    state_digest: str | None = None
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        movie_digest = str(manifest["movieSha256"])
        state_digest = str(manifest.get("stateSha256") or "") or None
    else:
        movie_digest = sha256(movie)
    plan = load_plan(plan_path)
    frames = expand_plan(plan)
    main = start_movie_replay(pid, movie)
    time.sleep(reload_wait)
    pine = Pine("127.0.0.1", port)
    try:
        initial = sample_player(pine)
        samples: list[dict[str, object]] = []
        for frame_index, command in enumerate(frames):
            frame_advance(main, settle)
            observed = sample_player(pine)
            samples.append({
                "frame": frame_index,
                "segment": command["label"],
                "segment_index": command["segment_index"],
                "local_frame": command["local_frame"],
                "left": list(command["left"]),
                "right": list(command["right"]),
                "buttons": list(command["buttons"]),
                "sample": observed,
            })
    finally:
        pine.close()
    capture = {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / PCSX2 input recording + PINE",
        "movieSha256": movie_digest,
        "stateSha256": state_digest,
        "initial": initial,
        "samples": samples,
    }
    raw_out.parent.mkdir(parents=True, exist_ok=True)
    raw_out.write_text(json.dumps(capture, indent=2) + "\n", encoding="utf-8")
    return capture

def _sequence_path(rows: list[dict[str, object]]) -> list[int]:
    result: list[int] = []
    for row in rows:
        value = int(row["sample"]["sequence"])
        if not result or result[-1] != value:
            result.append(value)
    return result

def derive(capture: dict[str, object]) -> dict[str, object]:
    rows = list(capture["samples"])
    segments: list[dict[str, object]] = []
    labels: list[str] = []
    for row in rows:
        label = str(row["segment"])
        if not labels or labels[-1] != label:
            labels.append(label)
    for label in labels:
        group = [row for row in rows if row["segment"] == label]
        first = group[0]
        last = group[-1]
        planar_steps = [
            math.hypot(float(row["sample"]["disp_x"]), float(row["sample"]["disp_y"]))
            for row in group
        ]
        max_planar = max(planar_steps)
        dx = float(last["sample"]["pos_x"]) - float(first["sample"]["pos_x"])
        dy = float(last["sample"]["pos_y"]) - float(first["sample"]["pos_y"])
        segments.append({
            "label": label,
            "frames": len(group),
            "left": first["left"],
            "right": first["right"],
            "buttons": first.get("buttons", []),
            "netPlanarPositionDelta": math.hypot(dx, dy),
            "maxPlanarDisplacementPerUpdate": max_planar,
            "planarDisplacementPerUpdate": planar_steps,
            "sequenceSamples": [int(row["sample"]["sequence"]) for row in group],
            "yawStart": first["sample"]["yaw"],
            "yawEnd": last["sample"]["yaw"],
            "targetYawEnd": last["sample"]["target_yaw"],
            "sequencePath": _sequence_path(group),
        })
    candidate_offsets = list(rows[0]["sample"]["candidate_words"].keys()) if rows else []
    changing: list[dict[str, object]] = []
    for offset in candidate_offsets:
        values = {int(row["sample"]["candidate_words"][offset]) for row in rows}
        if len(values) > 1:
            changing.append({"offset": offset, "distinctWords": len(values)})
    return {
        "schema": 1,
        "authority": capture["authority"],
        "movieSha256": capture["movieSha256"],
        "stateSha256": capture.get("stateSha256"),
        "sampleCadence": "one PINE sample after each PCSX2 FrameAdvance(1)",
        "playerStateBase": f"0x{PLAYER_STATE:08x}",
        "playerMoby": f"0x{PLAYER_MOBY:08x}",
        "segments": segments,
        "sequencePath": _sequence_path(rows),
        "changingCandidateFields": changing,
    }

def write_derived(capture_path_value: Path, output: Path) -> dict[str, object]:
    raw_path = capture_path(capture_path_value)
    report = derive(json.loads(raw_path.read_text(encoding="utf-8")))
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    profile = sub.add_parser("prepare-profile")
    profile.add_argument("--source", type=Path, required=True)
    profile.add_argument("--out", type=Path, required=True)
    profile.add_argument("--pine-port", type=int, default=28099)
    movie = sub.add_parser("build")
    movie.add_argument("--plan", type=Path, required=True)
    movie.add_argument("--savestate", type=Path, required=True)
    movie.add_argument("--movie", type=Path, required=True)
    capture = sub.add_parser("capture")
    capture.add_argument("--pid", type=int, required=True)
    capture.add_argument("--pine-port", type=int, default=28099)
    capture.add_argument("--plan", type=Path, required=True)
    capture.add_argument("--movie", type=Path, required=True)
    capture.add_argument("--out", type=Path, required=True)
    capture.add_argument("--settle", type=float, default=0.05)
    capture.add_argument("--reload-wait", type=float, default=1.0)
    derived = sub.add_parser("derive")
    derived.add_argument("--capture", type=Path, required=True)
    derived.add_argument("--out", type=Path, required=True)
    return parser

def main() -> int:
    args = build_parser().parse_args()
    if args.command == "prepare-profile":
        result = prepare_profile(args.source, args.out, args.pine_port)
    elif args.command == "build":
        result = build_movie(args.plan, args.savestate, args.movie)
    elif args.command == "capture":
        result = capture_trial(
            args.pid, args.pine_port, args.plan, args.movie, args.out,
            args.settle, args.reload_wait,
        )
    else:
        result = write_derived(args.capture, args.out)
    if args.command == "capture":
        summary = {
            "schema": result["schema"],
            "authority": result["authority"],
            "movieSha256": result["movieSha256"],
            "stateSha256": result.get("stateSha256"),
            "samples": len(result["samples"]),
            "out": str(args.out),
        }
        print(json.dumps(summary, indent=2))
    else:
        print(json.dumps(result, indent=2))
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
