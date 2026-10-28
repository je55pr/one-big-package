#!/usr/bin/env python3
"""Summarise GS depth/alpha state changes and draw batches in a PCSX2 GS dump.

Diagnostic only: does not assume that initial GS state is the state of a later
draw. Raw dumps are never written into source-controlled directories.
"""
from __future__ import annotations

import argparse
import ctypes
import json
import lzma
import os
from pathlib import Path
import struct
from dataclasses import dataclass

REGISTER_NAMES = {
    0x06: "TEX0_1", 0x07: "TEX0_2",
    0x14: "TEX1_1", 0x15: "TEX1_2",
    0x1A: "PRMODECONT", 0x1B: "PRMODE",
    0x42: "ALPHA_1", 0x43: "ALPHA_2",
    0x47: "TEST_1", 0x48: "TEST_2",
    0x4E: "ZBUF_1", 0x4F: "ZBUF_2",
}
VERTEX_REGS = {0x04, 0x05, 0x0C, 0x0D}
MAX_DECOMPRESSED_BYTES = 512 * 1024 * 1024
ZTEST_NAMES = ("never", "always", "gequal", "greater")
ATST_NAMES = ("never", "always", "less", "lequal", "equal", "gequal", "greater", "notequal")


def depth_state(test: int | None, zbuf: int | None) -> dict:
    if test is None:
        return {"known": False}
    return {
        "known": True,
        "depth_test_enabled": bool((test >> 16) & 1),
        "depth_function": ZTEST_NAMES[(test >> 17) & 3],
        "alpha_test_enabled": bool(test & 1),
        "alpha_function": ATST_NAMES[(test >> 1) & 7],
        "alpha_reference": (test >> 4) & 255,
        "alpha_fail": (test >> 12) & 3,
        "depth_write_enabled": None if zbuf is None else not bool((zbuf >> 32) & 1),
        "depth_format": None if zbuf is None else (zbuf >> 24) & 15,
    }


def decompress(path: Path) -> bytes:
    data = path.read_bytes()
    name = path.name.lower()
    if name.endswith(".xz"):
        if not data:
            raise ValueError("Empty XZ dump")
        output = bytearray()
        remaining = data
        # PCSX2 may concatenate XZ streams. Bound each decompression call
        # before materialising its output, across every stream combined.
        while remaining:
            decoder = lzma.LZMADecompressor()
            compressed = remaining
            while not decoder.eof:
                available = MAX_DECOMPRESSED_BYTES - len(output) + 1
                output.extend(decoder.decompress(compressed, max_length=available))
                if len(output) > MAX_DECOMPRESSED_BYTES:
                    raise ValueError("Decompressed GS dump exceeds size limit")
                compressed = b""
                if not decoder.eof and decoder.needs_input:
                    raise ValueError("Truncated XZ stream")
            remaining = decoder.unused_data
            # XZ permits zero-byte Stream Padding in multiples of four bytes.
            padding = len(remaining) - len(remaining.lstrip(b"\x00"))
            if padding % 4:
                raise ValueError("Invalid XZ stream padding length")
            remaining = remaining[padding:]
        return bytes(output)
    if not name.endswith(".zst"):
        return data
    try:
        import zstandard  # type: ignore[import-not-found]
        return zstandard.ZstdDecompressor().decompress(data, max_output_size=MAX_DECOMPRESSED_BYTES)
    except ImportError:
        pass
    # PCSX2 bundles the Zstandard DLL on Windows. Avoid installing packages
    # or altering the emulator's existing compression settings.
    dll = Path(os.environ.get("PCSX2_ZSTD_DLL", r"C:\Program Files\PCSX2\zstd.dll"))
    if not dll.is_file():
        raise RuntimeError("Zstd dump: install Python zstandard, or set PCSX2_ZSTD_DLL")
    lib = ctypes.CDLL(str(dll))
    lib.ZSTD_decompressBound.argtypes = (ctypes.c_void_p, ctypes.c_size_t)
    lib.ZSTD_decompressBound.restype = ctypes.c_ulonglong
    lib.ZSTD_decompress.argtypes = (ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t)
    lib.ZSTD_decompress.restype = ctypes.c_size_t
    lib.ZSTD_isError.argtypes = (ctypes.c_size_t,)
    lib.ZSTD_isError.restype = ctypes.c_uint
    src = ctypes.create_string_buffer(data)
    bound = lib.ZSTD_decompressBound(src, len(data))
    if not 0 < bound <= MAX_DECOMPRESSED_BYTES:
        raise ValueError(f"Compressed GS dump has invalid/unreasonably large bound: {bound}")
    dst = ctypes.create_string_buffer(bound)
    length = lib.ZSTD_decompress(dst, bound, src, len(data))
    if lib.ZSTD_isError(length):
        raise ValueError("PCSX2 Zstd decompression failed")
    return dst.raw[:length]


@dataclass
class Tag:
    loops: int
    mode: int
    regs: tuple[int, ...]
    remaining: int
    pad: int


class GifPath:
    def __init__(self, audit: "GsAudit", index: int):
        self.audit = audit
        self.index = index
        self.tail = b""
        self.tag: Tag | None = None
        self.image_bytes = 0

    def feed(self, chunk: bytes) -> None:
        data = self.tail + chunk
        cursor = 0
        while cursor < len(data):
            if self.image_bytes:
                count = min(self.image_bytes, len(data) - cursor)
                cursor += count
                self.image_bytes -= count
                continue
            if self.tag is None:
                if len(data) - cursor < 16:
                    break
                lo, hi = struct.unpack_from("<QQ", data, cursor)
                cursor += 16
                loops = lo & 0x7FFF
                mode = (lo >> 58) & 3
                nreg = (lo >> 60) & 15 or 16
                regs = tuple((hi >> (i * 4)) & 15 for i in range(nreg))
                if loops and (lo >> 46) & 1:
                    self.audit.set_register(0x00, (lo >> 47) & 0x7FF, self.index)
                if mode >= 2:
                    self.image_bytes = loops * 16
                    continue
                remaining = loops * nreg
                self.tag = Tag(loops, mode, regs, remaining, (remaining & 1) * 8 if mode == 1 else 0)
                if not remaining and not self.tag.pad:
                    self.tag = None
                continue
            t = self.tag
            if t.remaining:
                width = 16 if t.mode == 0 else 8
                if len(data) - cursor < width:
                    break
                n = t.loops * len(t.regs) - t.remaining
                reg = t.regs[n % len(t.regs)]
                value = struct.unpack_from("<Q", data, cursor)[0]
                if t.mode == 0:
                    if reg == 0xE:  # GIF packed A+D, GS address in high qword
                        address = struct.unpack_from("<Q", data, cursor + 8)[0] & 0xFF
                        self.audit.set_register(address, value, self.index)
                        if address in VERTEX_REGS:
                            self.audit.vertex(self.index, "ad")
                    elif reg in (0x00, 0x06, 0x07):
                        self.audit.set_register(reg, value, self.index)
                    elif reg in VERTEX_REGS:
                        self.audit.vertex(self.index, "packed")
                elif reg in (0x00, 0x06, 0x07):
                    self.audit.set_register(reg, value, self.index)
                elif reg in VERTEX_REGS:
                    self.audit.vertex(self.index, "reglist")
                cursor += width
                t.remaining -= 1
            if t.remaining == 0:
                if t.pad:
                    if len(data) - cursor < t.pad:
                        break
                    cursor += t.pad
                self.tag = None
        self.tail = data[cursor:]


class GsAudit:
    def __init__(self):
        self.registers: dict[int, int] = {}
        self.events: list[dict] = []
        self.batches: list[dict] = []
        self.frame = 0
        self.packet = 0
        self.paths = {n: GifPath(self, n) for n in range(4)}

    def set_register(self, address: int, value: int, path: int):
        if address == 0x00:
            self.registers[0x00] = value
        if address not in REGISTER_NAMES:
            return
        old = self.registers.get(address)
        self.registers[address] = value
        if old != value:
            self.events.append({
                "frame": self.frame, "packet": self.packet, "path": path,
                "register": REGISTER_NAMES[address], "value": f"0x{value:016x}",
            })

    def vertex(self, path: int, source: str):
        prim = self.registers.get(0x00)
        # PRMODECONT.AC selects the source of attribute bits 3-10.
        # PRIM always supplies the primitive type in bits 0-2.
        use_prim_attributes = bool(self.registers.get(0x1A, 1) & 1)
        attributes = prim if use_prim_attributes else self.registers.get(0x1B)
        effective_prim = (None if prim is None or attributes is None else
                          (prim & 7) | (attributes & ~7))
        context = ((attributes or 0) >> 9) & 1
        test = self.registers.get(0x47 + context)
        zbuf = self.registers.get(0x4E + context)
        tex0 = self.registers.get(0x06 + context)
        alpha = self.registers.get(0x42 + context)
        key = (self.frame, context, test, zbuf, tex0, alpha, effective_prim)
        if self.batches and self.batches[-1]["_key"] == key:
            self.batches[-1]["vertex_writes"] += 1
            sources = self.batches[-1]["vertex_sources"]
            sources[source] = sources.get(source, 0) + 1
            return
        self.batches.append({
            "_key": key, "frame": self.frame, "packet_start": self.packet,
            "path": path, "context": context,
            "vertex_writes": 1, "vertex_sources": {source: 1},
            "gs_state": depth_state(test, zbuf),
            "primitive": None if prim is None else prim & 7,
            "primitive_attributes_source": "PRIM" if use_prim_attributes else "PRMODE",
            "texture_mapping_enabled": None if attributes is None else bool((attributes >> 4) & 1),
            "alpha_blending_enabled": None if attributes is None else bool((attributes >> 6) & 1),
            "texture_base": None if tex0 is None else tex0 & 0x3FFF,
            "tex0": None if tex0 is None else f"0x{tex0:016x}",
            "alpha": None if alpha is None else f"0x{alpha:016x}",
            "test": None if test is None else f"0x{test:016x}",
            "zbuf": None if zbuf is None else f"0x{zbuf:016x}",
        })

    def analyse(self, data: bytes) -> dict:
        if len(data) < 48 or struct.unpack_from("<I", data, 0)[0] != 0xFFFFFFFF:
            raise ValueError("Not a new-format PCSX2 GS dump")
        header_bytes = struct.unpack_from("<I", data, 4)[0]
        if header_bytes < 36 or header_bytes > len(data) - 8:
            raise ValueError("Invalid PCSX2 GS header length")
        version, state_size, serial_at, serial_bytes, crc, width, height, image_at, image_size = struct.unpack_from(
            "<9I", data, 8)
        if serial_at + serial_bytes > header_bytes or image_at + image_size > header_bytes:
            raise ValueError("Invalid serial/image offsets in GS header")
        serial = data[8 + serial_at:8 + serial_at + serial_bytes].decode("ascii", "replace")
        offset = 8 + header_bytes + state_size + 0x2000  # 8 KiB privileged registers
        if offset > len(data):
            raise ValueError("Truncated GS initial state")
        while offset < len(data):
            kind = data[offset]
            offset += 1
            if kind == 0:
                if offset + 5 > len(data):
                    raise ValueError("Truncated GS GIF transfer header")
                index, size = struct.unpack_from("<BI", data, offset)
                offset += 5
                if index not in self.paths or size > len(data) - offset:
                    raise ValueError("Invalid GS GIF transfer path/size")
                self.packet += 1
                self.paths[index].feed(data[offset:offset + size])
                offset += size
            elif kind == 1:
                if offset >= len(data):
                    raise ValueError("Truncated GS vsync")
                self.frame += 1
                offset += 1
            elif kind == 2:
                offset += 4
            elif kind == 3:
                offset += 0x2000
            else:
                raise ValueError(f"Unknown GS dump packet type {kind} at {offset-1}")
            if offset > len(data):
                raise ValueError("Truncated GS dump packet payload")
        summary = [
            {k: v for k, v in batch.items() if k != "_key"}
            for batch in self.batches
        ]
        incomplete_paths = [
            index for index, path in self.paths.items()
            if path.tail or path.tag is not None or path.image_bytes
        ]
        return {
            "dump": {"serial": serial, "crc": f"{crc:08x}", "state_version": version,
                     "screenshot_size": [width, height], "frame_boundaries": self.frame,
                     "transfer_packets": self.packet},
            "depth_state_counts_by_vertex_writes": {
                name: sum(b["vertex_writes"] for b in summary
                          if b["gs_state"].get("depth_function", "unknown") == name)
                for name in (*ZTEST_NAMES, "unknown")
            },
            "incomplete_paths": incomplete_paths,
            "register_changes": self.events,
            "draw_batches": summary,
            "warning": "GS header initial register state is not decoded. These are vertex-write "
                       "batches grouped by observed state, not identified game objects or "
                       "individual draw calls. State is conclusive only when its relevant "
                       "GS registers were observed in streamed GIF packets.",
        }


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("dump", type=Path)
    p.add_argument("--out", type=Path, help="Private JSON report path, preferably under captures/")
    p.add_argument("--last", type=int, default=20, help="Last number of draw batches to print")
    args = p.parse_args()
    result = GsAudit().analyse(decompress(args.dump))
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
        print(f"Private report: {args.out}")
    print(json.dumps({
        "dump": result["dump"], "depth_state_counts_by_vertex_writes":
            result["depth_state_counts_by_vertex_writes"],
        "register_changes": len(result["register_changes"]),
        "incomplete_paths": result["incomplete_paths"],
        "draw_batches": len(result["draw_batches"]),
        "last_batches": result["draw_batches"][-args.last:] if args.last > 0 else [],
        "warning": result["warning"],
    }, indent=2))


if __name__ == "__main__":
    main()
