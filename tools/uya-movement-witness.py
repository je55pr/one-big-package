#!/usr/bin/env python3
"""Capture controlled UYA movement witnesses from PCSX2 PINE.

This tool contains no retail payload. It reads a fixed set of runtime words from
SCUS-97353 and drives only keyboard bindings in a disposable PCSX2 profile.
Raw captures belong under the repository's ignored captures/ directory.
"""
from __future__ import annotations

import argparse
import ctypes
import json
import math
import socket
import struct
import time
from ctypes import wintypes
from pathlib import Path

BASE = 0x001A4BE0
NTSC_HZ = 59.94
READ32 = 2
FIELDS = {
    "state": (0x25C4, "u32"),
    "player": (0x25C0, "u32"),
    "pos_x": (0x80, "f32"),
    "pos_y": (0x84, "f32"),
    "pos_z": (0x88, "f32"),
    "vel_x": (0xF0, "f32"),
    "vel_y": (0xF4, "f32"),
    "vel_z": (0xF8, "f32"),
    "input_x": (0x2260, "f32"),
    "input_y": (0x2264, "f32"),
    "magnitude": (0x2838, "f32"),
    "angle": (0x2840, "f32"),
}
# The live-player object exposes a position-like vector at +0x10/+0x14/+0x18.
# Ground traces align it with the player-global position; jump traces retain both
# independently because update ordering can separate them. +0xF8 is a yaw candidate.
LIVE_OFFSETS = {
    "live_pos_x": (0x10, "f32"),
    "live_pos_y": (0x14, "f32"),
    "live_pos_z": (0x18, "f32"),
    "live_orientation_candidate": (0xF8, "f32"),
}
VK = {
    "w": 0x57, "a": 0x41, "s": 0x53, "d": 0x44,
    "x": 0x58, "1": 0x31,
    "f3": 0x72,
}
SCENARIOS = {
    "accel-release": [(15, ()), (45, ("w",)), (60, ())],
    "jump-tap": [(15, ()), (6, ("x",)), (84, ())],
    "jump-hold": [(15, ()), (24, ("x",)), (84, ())],
    "turn-90": [(15, ()), (14, ("d",)), (45, ())],
    "turn-180": [(15, ()), (60, ("s",)), (45, ())],
    "normal-right": [(15, ()), (30, ("d",)), (45, ())],
    "l2-right": [(15, ()), (30, ("1", "d")), (45, ())],
}


def bits_to_float(value: int) -> float:
    return struct.unpack("<f", struct.pack("<I", value))[0]


class Pine:
    def __init__(self, port: int):
        self.sock = socket.create_connection(("127.0.0.1", port), 3.0)
        self.sock.settimeout(3.0)

    def close(self) -> None:
        self.sock.close()

    def _exact(self, size: int) -> bytes:
        out = bytearray()
        while len(out) < size:
            chunk = self.sock.recv(size - len(out))
            if not chunk:
                raise ConnectionError("PINE socket closed")
            out.extend(chunk)
        return bytes(out)

    def read32(self, addresses: list[int]) -> tuple[int, ...]:
        payload = b"".join(struct.pack("<BI", READ32, address) for address in addresses)
        self.sock.sendall(struct.pack("<I", 4 + len(payload)) + payload)
        response_size = struct.unpack("<I", self._exact(4))[0]
        body = self._exact(response_size - 4)
        if not body or body[0] != 0:
            raise RuntimeError(f"PINE response status {body[:1].hex() if body else 'empty'}")
        expected = 1 + 4 * len(addresses)
        if len(body) < expected:
            raise RuntimeError(f"short PINE response: {len(body)} < {expected}")
        return struct.unpack("<" + "I" * len(addresses), body[1:expected])


class WindowKeys:
    def __init__(self, pid: int):
        self.user32 = ctypes.windll.user32
        self.hwnd = self._find_window(pid)
        self.down: set[str] = set()

    def _find_window(self, pid: int) -> int:
        hits: list[tuple[int, str]] = []
        user32 = self.user32

        @ctypes.WINFUNCTYPE(ctypes.c_bool, wintypes.HWND, wintypes.LPARAM)
        def callback(hwnd: int, _lparam: int) -> bool:
            process_id = wintypes.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
            if process_id.value == pid and user32.IsWindowVisible(hwnd):
                length = user32.GetWindowTextLengthW(hwnd)
                title = ctypes.create_unicode_buffer(length + 1)
                user32.GetWindowTextW(hwnd, title, length + 1)
                hits.append((hwnd, title.value))
            return True

        user32.EnumWindows(callback, 0)
        if not hits:
            raise RuntimeError(f"no visible window for PCSX2 pid {pid}")
        return max(hits, key=lambda item: len(item[1]))[0]

    def _post(self, name: str, is_down: bool) -> None:
        vk = VK[name]
        scan = self.user32.MapVirtualKeyW(vk, 0)
        lparam = 1 | (scan << 16)
        if not is_down:
            lparam |= (1 << 30) | (1 << 31)
        self.user32.PostMessageW(self.hwnd, 0x100 if is_down else 0x101, vk, lparam)

    def set_keys(self, wanted: tuple[str, ...]) -> None:
        wanted_set = set(wanted)
        for name in sorted(self.down - wanted_set):
            self._post(name, False)
        for name in sorted(wanted_set - self.down):
            self._post(name, True)
        self.down = wanted_set

    def tap(self, name: str, seconds: float = 0.12) -> None:
        self._post(name, True)
        time.sleep(seconds)
        self._post(name, False)

    def release_all(self) -> None:
        self.set_keys(())


def read_sample(pine: Pine) -> dict[str, object]:
    global_names = list(FIELDS)
    global_addresses = [BASE + FIELDS[name][0] for name in global_names]
    global_values = pine.read32(global_addresses)
    sample: dict[str, object] = {}
    for name, raw in zip(global_names, global_values):
        sample[name] = raw if FIELDS[name][1] == "u32" else bits_to_float(raw)
    player = int(sample["player"])
    if player:
        live_names = list(LIVE_OFFSETS)
        live_values = pine.read32([player + LIVE_OFFSETS[name][0] for name in live_names])
        for name, raw in zip(live_names, live_values):
            sample[name] = raw if LIVE_OFFSETS[name][1] == "u32" else bits_to_float(raw)
    return sample


def scenario_frames(name: str) -> list[tuple[str, ...]]:
    result: list[tuple[str, ...]] = []
    for count, keys in SCENARIOS[name]:
        result.extend([keys] * count)
    return result


def capture(args: argparse.Namespace) -> dict[str, object]:
    keys = WindowKeys(args.pid)
    keys.release_all()
    if args.reload_state:
        keys.tap("f3")
        time.sleep(args.reload_wait)
    pine = Pine(args.port)
    try:
        warm = read_sample(pine)
        if not warm["player"]:
            raise RuntimeError("live-player pointer is zero; gameplay is not ready")
        frames = scenario_frames(args.scenario)
        rows: list[dict[str, object]] = []
        start = time.perf_counter()
        interval = 1.0 / args.hz
        last_keys: tuple[str, ...] = ()
        for frame, wanted in enumerate(frames):
            deadline = start + frame * interval
            delay = deadline - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            if wanted != last_keys:
                keys.set_keys(wanted)
                last_keys = wanted
            sampled_at = time.perf_counter()
            row = read_sample(pine)
            row["frame"] = frame
            row["t_s"] = sampled_at - start
            row["keys"] = list(wanted)
            rows.append(row)
        keys.release_all()
        end = time.perf_counter()
    finally:
        keys.release_all()
        pine.close()
    return {
        "authority": {"serial": "SCUS-97353", "version": "1.00", "crc": "45FE0CC4"},
        "base": "0x001A4BE0",
        "requested_hz": args.hz,
        "sampling": "wall-clock",
        "scenario": args.scenario,
        "samples": rows,
        "capture_wall_s": end - start,
    }

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pid", type=int, required=True, help="PCSX2 process id")
    parser.add_argument("--port", type=int, default=28011)
    parser.add_argument("--hz", type=float, default=NTSC_HZ)
    parser.add_argument("--scenario", choices=sorted(SCENARIOS), required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--reload-state", action="store_true")
    parser.add_argument("--reload-wait", type=float, default=1.25)
    args = parser.parse_args()
    result = capture(args)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + "\n")
    samples = result["samples"]
    dt = [samples[i]["t_s"] - samples[i - 1]["t_s"] for i in range(1, len(samples))]
    print(json.dumps({
        "scenario": args.scenario,
        "samples": len(samples),
        "wall_s": result["capture_wall_s"],
        "mean_sample_hz": 1.0 / (sum(dt) / len(dt)) if dt else math.nan,
        "out": str(args.out),
    }))


if __name__ == "__main__":
    main()
