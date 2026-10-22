#!/usr/bin/env python3
"""Bounded R&C1 retail HUD bank inspection/extraction. Requires user's own NTSC-U ISO.

No original image/font data is stored or distributed by OBP. Keep output in
game/.retail-ui (gitignored). The native bank has animated sprite definitions.
"""
import argparse
import json
import struct
import zlib
from pathlib import Path

SECTOR = 2048
INDEX_LBA = 1500
INDEX_BYTES = 0x2960
HUD_CHUNKS_OFFSET = 0x14F8
MAX_CHUNK = 8 * 1024 * 1024
MAX_INFLATED = 32 * 1024 * 1024


def at(data, offset, size, label):
    if offset < 0 or size < 0 or offset > len(data) or size > len(data) - offset:
        raise ValueError(f"{label}: out of bounds at {offset:#x}+{size:#x}")
    return data[offset:offset + size]


def u16(b, o):
    return struct.unpack_from("<H", at(b, o, 2, "u16"))[0]


def u32(b, o):
    return struct.unpack_from("<I", at(b, o, 4, "u32"))[0]


def read_disc(stream, offset, size, disc_size):
    if size <= 0 or offset < 0 or size > disc_size - offset:
        raise ValueError("invalid disc range")
    stream.seek(offset)
    data = stream.read(size)
    if len(data) != size:
        raise ValueError("short disc read")
    return data


def decode_wad(source):
    if source[:3] != b"WAD" or len(source) < 16:
        raise ValueError("not a WAD compressed block")
    end = u32(source, 3)
    if end < 16 or end > len(source):
        raise ValueError(f"invalid WAD declared size {end}")
    pos, dst, literal = 16, bytearray(), False

    def byte():
        nonlocal pos
        if pos >= end:
            raise ValueError("short WAD packet")
        b = source[pos]
        pos += 1
        return b

    def output_lit(n):
        nonlocal pos
        if n > end - pos or len(dst) + n > MAX_INFLATED:
            raise ValueError("WAD literal overrun")
        dst.extend(source[pos:pos+n])
        pos += n

    while pos < end:
        flag = byte()
        if flag < 0x10:
            if literal:
                raise ValueError("consecutive WAD literals")
            output_lit(flag + 3 if flag else byte() + 18)
            literal = True
            continue
        literal = False
        ended = False
        if flag < 0x20:
            n = flag & 7
            if not n:
                n = byte() + 7
            b0, b1 = byte(), byte()
            lookback = len(dst) - ((flag & 8) << 11) - (b1 << 6) - (b0 >> 2)
            if lookback != len(dst):
                n += 2
                lookback -= 0x4000
            elif n != 1:
                pos = 16 + (((pos - 16) + 0xFFF) & ~0xFFF)
                ended = True
        elif flag < 0x40:
            n = flag & 31
            if not n:
                n = byte() + 31
            n += 2
            b1, b2 = byte(), byte()
            lookback = len(dst) - (b2 << 6) - (b1 >> 2) - 1
        else:
            b1 = byte()
            lookback = len(dst) - (b1 << 3) - ((flag >> 2) & 7) - 1
            n = (flag >> 5) + 1
        if ended:
            continue
        if n != 1:
            if lookback < 0 or lookback >= len(dst) or len(dst) + n > MAX_INFLATED:
                raise ValueError("WAD back-reference outside output")
            for k in range(n):
                dst.append(dst[lookback + k])
        output_lit(source[pos - 2] & 3)
    return bytes(dst)


def load_bank(iso):
    with open(iso, "rb") as stream:
        size = stream.seek(0, 2)
        index = read_disc(stream, INDEX_LBA * SECTOR, INDEX_BYTES, size)
        if (u32(index, 0), u32(index, 4)) != (1, INDEX_BYTES):
            raise ValueError("unsupported R&C1 disc index")
        ranges, chunks = [], []
        for i in range(6):
            lba = u32(index, HUD_CHUNKS_OFFSET + i * 8)
            sectors = u32(index, HUD_CHUNKS_OFFSET + i * 8 + 4)
            if not 0 < sectors * SECTOR <= MAX_CHUNK:
                raise ValueError(f"HUD chunk {i} has invalid length")
            raw = read_disc(stream, lba * SECTOR, sectors * SECTOR, size)
            chunk = raw if i == 0 else decode_wad(raw)
            ranges.append(dict(slot=i, lba=lba, sectors=sectors, compressedBytes=len(raw),
                               decodedBytes=len(chunk)))
            chunks.append(chunk)
    return chunks, ranges


def parse_bank(header):
    if len(header) < 0x100:
        raise ValueError("HUD bank header truncated")
    a_off, f_off, p_off, i_off = (u32(header, x) for x in (4, 8, 12, 16))
    if not 0xB0 <= a_off < f_off < p_off < i_off <= len(header):
        raise ValueError("invalid HUD table order")
    pal_ends = [u32(header, 0x14 + x * 4) for x in range(8)]
    img_ends = [u32(header, 0x34 + x * 4) for x in range(8)]
    if any(x > y for x, y in zip([x for x in pal_ends if x], [x for x in pal_ends if x][1:])):
        raise ValueError("invalid palette end order")
    if any(x > y for x, y in zip([x for x in img_ends if x], [x for x in img_ends if x][1:])):
        raise ValueError("invalid image end order")
    palette_count, image_count = max(pal_ends), max(img_ends)
    if p_off + palette_count * 8 > i_off or i_off + image_count * 8 > len(header):
        raise ValueError("HUD page arrays exceed header")
    animations = []
    for o in range(a_off, f_off, 8):
        id_, count, start, flags, pad = struct.unpack_from("<3H2B", header, o)
        if id_ == 0xffff:
            break
        if start + count > (p_off - f_off) // 4:
            raise ValueError("HUD animation frame reference out of range")
        animations.append(dict(id=id_, frames=count, start=start, flags=flags))
    else:
        raise ValueError("no HUD animation sentinel")
    refs = [dict(palette=u16(header, f_off + 4 * j),
                 image=u16(header, f_off + 4 * j + 2))
            for j in range((p_off-f_off)//4)]
    palettes = [page(header, p_off+j*8) for j in range(palette_count)]
    images = [page(header, i_off+j*8) for j in range(image_count)]
    for anim in animations:
        for index in range(anim["start"], anim["start"] + anim["frames"]):
            ref = refs[index]
            if ref["palette"] >= palette_count or ref["image"] >= image_count:
                raise ValueError(f"animation {anim['id']:#x} invalid page index")
    return dict(animations=animations, frames=refs, palettes=palettes, images=images,
                paletteBankEnds=pal_ends, imageBankEnds=img_ends,
                offsets=dict(animations=a_off, frames=f_off, palettes=p_off, images=i_off))


def page(data, o):
    address, gs, w, h = struct.unpack_from("<IHBB", data, o)
    if w > 11 or h > 11:
        raise ValueError("HUD page dimensions too large")
    return dict(offset=address & 0x7fffffff, unresolved=bool(address & 0x80000000),
                gsBlock=gs, width=1 << w, height=1 << h)


def png_rgba(width, height, pixels):
    if len(pixels) != width*height*4:
        raise ValueError("invalid PNG pixel count")
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind+data))
    scan = b"".join(b"\0" + pixels[y*width*4:(y+1)*width*4] for y in range(height))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">2I5B", width, height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(scan, 9)) + chunk(b"IEND", b"")


def decode_pixels(pix, pal, w, h):
    # Texture pixels in the bank are index8 in native upload order; validation
    # of this assumption against in-game screenshots is tracked separately.
    def swizzle(i):
        return i ^ 24 if (i & 8) != ((i & 16) >> 1) else i
    out = bytearray(w*h*4)
    for i in range(w*h):
        src = 4*swizzle(pix[i])
        r,g,b,a = pal[src:src+4]
        out[4*i:4*i+4] = bytes((r,g,b, min(255, a*2)))
    return bytes(out)


def extract_bank(iso, target, images=True):
    chunks, ranges = load_bank(iso)
    state = parse_bank(chunks[0])
    report = dict(format="rac1-ui-bank-v1", authority="SCUS-97199 NTSC-U original",
                  chunks=ranges, animations=state["animations"],
                  paletteBankEnds=state["paletteBankEnds"], imageBankEnds=state["imageBankEnds"],
                  offsets=state["offsets"], frameCount=len(state["frames"]),
                  paletteCount=len(state["palettes"]), imageCount=len(state["images"]),
                  exportedFrames=0, exportErrors=[])
    target.mkdir(parents=True, exist_ok=True)
    if images:
        sprites = target / "sprites"
        sprites.mkdir(exist_ok=True)
        # Native bank 0 is always resident. Other banks may require secondary
        # streaming; do not misinterpret their compressed payload as bank 0.
        for animation in state["animations"]:
            for local_frame in range(animation["frames"]):
                ix = animation["start"] + local_frame
                frame = state["frames"][ix]
                pp = state["palettes"][frame["palette"]]
                ip = state["images"][frame["image"]]
                pal_bank = next((j for j, end in enumerate(state["paletteBankEnds"]) if end and frame["palette"] < end), None)
                img_bank = next((j for j, end in enumerate(state["imageBankEnds"]) if end and frame["image"] < end), None)
                # Retail banks 0,1,4 are backed by WAD chunks 1,2,5.
                bank_chunk = {0: 1, 1: 2, 4: 5}
                if pal_bank not in bank_chunk or img_bank not in bank_chunk:
                    continue
                buf = chunks[bank_chunk[img_bank]]
                palette_buf = chunks[bank_chunk[pal_bank]]
                try:
                    palette = at(palette_buf, pp["offset"], 1024, "palette")
                    pix = at(buf, ip["offset"], ip["width"]*ip["height"], "texture")
                    rgba = decode_pixels(pix, palette, ip["width"], ip["height"])
                    filename = f"{animation['id']:04x}-{local_frame}.png"
                    (sprites / filename).write_bytes(png_rgba(ip["width"], ip["height"], rgba))
                    report["exportedFrames"] += 1
                except ValueError as exc:
                    report["exportErrors"].append(dict(icon=animation["id"], frame=local_frame, error=str(exc)))
    (target / "manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report



def locate_boot_elf(stream, disc_size):
    """Minimal bounded ISO9660 root-directory resolver for the PS2 boot ELF."""
    pvd = read_disc(stream, 16*SECTOR, SECTOR, disc_size)
    if pvd[1:6] != b"CD001":
        raise ValueError("missing ISO9660 primary volume descriptor")
    root = at(pvd, 156, 34, "root directory record")
    root_lba, root_size = u32(root, 2), u32(root, 10)
    if root_size > 128*1024:
        raise ValueError("root directory unexpectedly large")
    data = read_disc(stream, root_lba*SECTOR, root_size, disc_size)
    cursor, entries = 0, {}
    while cursor < len(data):
        length = data[cursor]
        if not length:
            cursor = (cursor + SECTOR) & ~(SECTOR-1)
            continue
        rec = at(data, cursor, length, "root directory entry")
        name_length = rec[32]
        name = at(rec, 33, name_length, "file name").decode("ascii", errors="replace")
        entries[name.upper().split(";")[0]] = (u32(rec, 2), u32(rec, 10))
        cursor += length
    if "SYSTEM.CNF" not in entries:
        raise ValueError("missing SYSTEM.CNF")
    lba, length = entries["SYSTEM.CNF"]
    config = read_disc(stream, lba*SECTOR, length, disc_size).decode("ascii")
    import re
    boot = re.search(r"BOOT2?\s*=\s*cdrom0:\\([^;\s]+)", config, re.I)
    if not boot:
        raise ValueError("unresolved boot ELF path")
    name = boot.group(1).upper()
    if name not in entries or name != "SCUS_971.99":
        raise ValueError("unexpected executable for R&C1")
    lba, size = entries[name]
    return read_disc(stream, lba*SECTOR, size, disc_size)


def elf_virtual(elf, address, size):
    if elf[:4] != b"\x7fELF" or elf[4:6] != b"\x01\x01":
        raise ValueError("not an ELF32 LE executable")
    # The ELF32 header stores phentsize at +42, phnum at +44.
    table, entry_size, count = u32(elf, 28), u16(elf, 42), u16(elf, 44)
    for i in range(count):
        o = table + i*entry_size
        typ, offset, va, pa, file_size, memory_size = struct.unpack_from("<6I", at(elf, o, 24, "program header"))
        if typ == 1 and va <= address and size <= file_size - (address-va) and address-va <= file_size:
            return at(elf, offset+address-va, size, "ELF virtual range")
    raise ValueError(f"ELF address {address:#x} not backed by a file segment")


def read_veldin_core(stream, disc_size):
    index = read_disc(stream, INDEX_LBA*SECTOR, INDEX_BYTES, disc_size)
    header_lba = u32(index, 0x28c8)
    level_header = read_disc(stream, header_lba*SECTOR, 0x28, disc_size)
    level_lba, level_sectors = u32(level_header, 8), u32(level_header, 12)
    level_base = level_lba*SECTOR
    level_len = level_sectors*SECTOR
    level_dir = read_disc(stream, level_base, 0x58, disc_size)
    def level_part(slot, cap):
        relative, length = struct.unpack_from("<2i", level_dir, slot*8)
        if length < 0 or length > cap or relative < 0 or relative+length > level_len:
            raise ValueError("invalid level subrange")
        return read_disc(stream, level_base+relative, length, disc_size)
    index_data = level_part(2, 4*1024*1024)
    assets = decode_wad(level_part(10, 16*1024*1024))
    return index_data, assets


def extract_font_metrics(iso, target):
    with open(iso, "rb") as stream:
        size = stream.seek(0, 2)
        executable = locate_boot_elf(stream, size)
        index, assets = read_veldin_core(stream, size)
    tables = [("regular", 0x001DF050), ("small", 0x001DF3F0), ("large", 0x001DF790)]
    metrics = {}
    for name, address in tables:
        raw = elf_virtual(executable, address, 0x3a0)
        glyphs = []
        for i in range(len(raw)//4):
            u, v, top, advance = struct.unpack_from("<2B2b", raw, i*4)
            glyphs.append(dict(code=i, u=u, v=v, top=top, advance=advance))
        metrics[name] = glyphs
    # The per-level FX table stores 16-byte (palette offset, pixel offset,
    # width, height) entries. The static executable table at 0x18d440 is
    # runtime-populated, so reading its file-backed zeros would be wrong.
    fx_count, fx_offset = u32(index, 0x58), u32(index, 0x5c)
    texture_base = u32(index, 0x68)
    if fx_count < 4 or fx_count > 256:
        raise ValueError("unexpected Veldin FX texture count")
    effects = []
    for i in range(fx_count):
        pal, pix, width, height = struct.unpack("<4I", at(index, fx_offset+16*i, 16, "FX texture"))
        if not (0 < width <= 2048 and 0 < height <= 2048):
            raise ValueError(f"invalid FX texture {i} dimensions")
        if texture_base+pal+1024 > len(assets) or texture_base+pix+width*height > len(assets):
            raise ValueError(f"invalid FX texture {i} data range")
        effects.append(dict(index=i, texelOffset=pix, paletteOffset=pal,
                            width=width, height=height))
    target.mkdir(parents=True, exist_ok=True)
    font_dir = target / "fonts"
    font_dir.mkdir(exist_ok=True)
    atlas_reports = []
    for effect_index, name in ((1, "regular"), (2, "small"), (3, "large")):
        tex = effects[effect_index]
        pal = at(assets, texture_base+tex["paletteOffset"], 1024, "font palette")
        pix = at(assets, texture_base+tex["texelOffset"], tex["width"]*tex["height"], "font pixels")
        rgba = decode_pixels(pix, pal, tex["width"], tex["height"])
        (font_dir / f"{name}.png").write_bytes(png_rgba(tex["width"], tex["height"], rgba))
        atlas_reports.append(dict(style=name, textureIndex=effect_index, width=tex["width"],
                                  height=tex["height"]))
        # Godot can load AngelCode BMFont files without a TTF/OTF. Only native
        # printable single-byte glyphs are mapped; 8..15 stay colour controls.
        glyphs = metrics[name]
        chars = []
        for g in glyphs:
            ch = g["code"]
            if ch < 32 or g["advance"] <= 0:
                continue
            if g["u"]+16 > tex["width"] or g["v"]+16 > tex["height"]:
                raise ValueError(f"out-of-range {name} glyph {ch:#x}")
            chars.append(
                f'char id={ch} x={g["u"]} y={g["v"]} width=16 height=16 '
                f'xoffset=0 yoffset={g["top"]} xadvance={g["advance"]} page=0 chnl=15'
            )
        bmfont = [
            f'info face="RAC1 {name}" size=16 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=0 aa=0 padding=0,0,0,0 spacing=0,0',
            f'common lineHeight=18 base=14 scaleW={tex["width"]} scaleH={tex["height"]} pages=1 packed=0',
            f'page id=0 file="{name}.png"',
            f'chars count={len(chars)}',
            *chars,
        ]
        (font_dir / f"{name}.fnt").write_text("\n".join(bmfont)+"\n", encoding="utf-8")
    report = dict(encoding="rac1-font-glyph-metrics-v1",
                  note="Three retail bitmap glyph atlases + BMFont mapping, not TrueType/OpenType",
                  fontMetrics=metrics, effectTextures=effects, fontAtlases=atlas_reports,
                  veldinCoreHeaderWords=[u32(index, at_) for at_ in (0x60,0x64,0x68,0x6c,0x88,0x8c)],
                  coreAssetLength=len(assets))
    (target / "fonts.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report



def extract_localized_text(iso, target):
    """Bounded raw native text tables. Do not treat control bytes as UTF-8."""
    with open(iso, "rb") as stream:
        size = stream.seek(0, 2)
        toc = read_disc(stream, INDEX_LBA*SECTOR, INDEX_BYTES, size)
        lba, sectors = struct.unpack_from("<2I", toc, 0x1528)
        if sectors == 0 or sectors > 1024:
            raise ValueError("invalid localization archive length")
        archive = read_disc(stream, lba*SECTOR, sectors*SECTOR, size)
    # Game language offsets in the header, six localisation slots.
    starts = struct.unpack_from("<6I", archive)
    report = []
    for language, start in enumerate(starts):
        count, byte_count = struct.unpack("<2I", at(archive, start, 8, "locale header"))
        if count > 5000 or byte_count > len(archive) - start or count*16+8 > byte_count:
            raise ValueError(f"invalid locale {language} text table")
        entries = {}
        for ix in range(count):
            position = start + 8 + 16*ix
            text_rel, text_id = struct.unpack("<2I", at(archive, position, 8, "text entry"))
            text_pos = start + text_rel
            if text_pos < start + 8 + count*16 or text_pos >= start+byte_count:
                raise ValueError(f"locale {language} text id {text_id} points outside text data")
            terminator = archive.find(b"\x00", text_pos, start+byte_count)
            if terminator < 0 or terminator - text_pos > 4096:
                raise ValueError("unterminated or very long text")
            raw = archive[text_pos:terminator]
            entries[str(text_id)] = dict(rawHex=raw.hex(), latin1=raw.decode("latin-1"))
        (target / f"strings-language-{language}.json").write_text(
            json.dumps(entries, indent=2, ensure_ascii=True), encoding="utf-8")
        report.append(dict(language=language, entries=len(entries), totalRecords=count))
    return report


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("iso", type=Path)
    ap.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[1] / "game" / "retail-ui-cache")
    ap.add_argument("--no-images", action="store_true")
    args = ap.parse_args()
    result = extract_bank(args.iso, args.out, not args.no_images)
    fonts = extract_font_metrics(args.iso, args.out)
    strings = extract_localized_text(args.iso, args.out)
    print(json.dumps({**{k: v for k, v in result.items() if k in ("chunks","frameCount","paletteCount","imageCount","exportedFrames","exportErrors")},
                      "fontMetrics": {name: len(glyphs) for name, glyphs in fonts["fontMetrics"].items()},
                      "effectTextures": fonts["effectTextures"][:4],
                      "coreHeader": fonts["veldinCoreHeaderWords"], "localization": strings}, indent=2))


if __name__ == "__main__":
    main()
