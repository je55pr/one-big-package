# UYA retail TIE / shrub static-geometry compatibility census

Status: retail-confirmed compatibility evidence for the existing GC TIE and shrub class readers across four sampled UYA worlds. Public-derived semantic names and native-loader provenance remain separate questions.

## Authority and run

Retail authority image:

- Ratchet & Clank: Up Your Arsenal NTSC-U original
- serial `SCUS-97353`
- size `4,379,377,664` bytes
- full-image SHA-256 `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`
- bounded ToC authority window: LBA 1001, 2 MiB, SHA-256 `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`

Local census:

- pipeline `2827258569` / iid `351`
- job `16352020372`
- self-hosted runner `obp-local`
- rows `1, 8, 20, 50`
- repository check: `198/198` tests passed before the retail probes

The compatibility wrapper independently checks the public-derived class table bounds, class asset offsets, minimum class-header extent, and duplicate oClasses. It then invokes the unchanged GC TIE/shrub readers and requires the decoded oClass set to match every independently eligible candidate. Geometry is additionally checked for finite positions and class material texture IDs are compared against the independently retail-confirmed TIE/shrub texture counts.

## TIE classes

| Row | Declared / candidate / decoded | Vertices | Triangles | Public TIE textures | Texture IDs outside table | Geometry SHA-256 |
|---:|---|---:|---:|---:|---|---|
| 1 | 78 / 78 / 78 | 47,570 | 36,442 | 166 | none | `a8b230d7c276ea8cc5d50bc96fad874daf5894eb81106fea9e5249884f5ee3ff` |
| 8 | 32 / 32 / 32 | 24,624 | 19,992 | 57 | none | `f06b8070ae1ba2078e8e9356a91152a3d1631b189d5f5819330b8fc44f7fdbef` |
| 20 | 51 / 51 / 51 | 39,536 | 30,992 | 89 | none | `5c78833a1a2f25678c4e3994f7c607b669a0d0fcaa9ff07345522923c263b012` |
| 50 | 55 / 55 / 55 | 12,328 | 8,896 | 100 | none | `43111b389b0127ee03fbbd1d89414726fb1289077689e4f95f9d140bed3cc9b4` |

For every sampled TIE table:

- table lies wholly inside the decoded core index;
- invalid asset offsets: `0`;
- class headers outside the decoded asset blob: `0`;
- duplicate oClasses: `0`;
- every candidate oClass is emitted by the unchanged GC reader;
- non-finite position components: `0`;
- observed texture IDs remain inside the corresponding retail-confirmed TIE texture table.

Sampled class-space bounds:

| Row | Min XYZ | Max XYZ |
|---:|---|---|
| 1 | `(-126.0184, -82.4216, -21.8846)` | `(123.0725, 83.4177, 99.5020)` |
| 8 | `(-25.0483, -27.4940, -29.6550)` | `(25.0483, 32.8360, 24.7646)` |
| 20 | `(-20.4587, -20.4587, -46.9990)` | `(38.5988, 26.2492, 51.7112)` |
| 50 | `(-12.0172, -21.2609, -36.3983)` | `(36.0777, 21.1743, 15.7169)` |

## Shrub classes

| Row | Declared / candidate / decoded | Vertices | Triangles | Public shrub textures | Texture IDs outside table | Geometry SHA-256 |
|---:|---|---:|---:|---:|---|---|
| 1 | 22 / 22 / 22 | 3,698 | 2,986 | 55 | none | `bd649e87cb3b5e3f15a2fba8b9ce2f22a9523fecd5f93f9b30fbed5ac3409182` |
| 8 | 5 / 5 / 5 | 466 | 372 | 10 | none | `2706b5cc2f781da2f4a6778601f91c3bbffcdb1f36c3c46c524792770b7e7046` |
| 20 | 19 / 19 / 19 | 3,755 | 3,029 | 19 | none | `6ced276416bcfca84b6b5988696e4e76968451068e63cbf6f283d5ef7640f847` |
| 50 | 4 / 4 / 4 | 277 | 163 | 9 | none | `39fb68e2d5b567148a0d49b758cc3efa42be34686dfd918a5c93bd49545f99e4` |

For every sampled shrub table the same structural checks are clean: all declared entries are independently eligible, no duplicate oClasses are present, every candidate class is decoded by the unchanged GC reader, all geometry is finite, and all material texture IDs fit the corresponding retail-confirmed shrub texture table.

Sampled class-space bounds:

| Row | Min XYZ | Max XYZ |
|---:|---|---|
| 1 | `(-11.7488, -15.2023, -11.9833)` | `(11.7488, 15.2023, 26.2283)` |
| 8 | `(-1.0663, -1.7305, -1.0663)` | `(1.0663, 0.9708, 1.0663)` |
| 20 | `(-3.2838, -4.0630, -3.8710)` | `(3.2838, 3.2902, 3.8710)` |
| 50 | `(-2.0374, -2.0706, -5.8898)` | `(1.9574, 2.4834, 14.7929)` |

## Interpretation

The existing GC TIE and shrub class geometry readers are now retail-confirmed compatible with the sampled UYA class tables and asset data. Combined with the independent texture census, OBP can reuse the existing GC class-mesh and texture decoders for sampled UYA TIE/shrub reconstruction without weakening or forking them.

This does **not** prove that every UYA world is compatible, nor does it independently prove the public-derived semantic names `tieClasses` / `shrubClasses`, the enclosing `0x60 / 0x58 / 0xbc` layout names, or native-loader provenance of the hidden ToC lead. Those provenance questions remain explicit.

## Next target

Identify and validate the UYA TIE/shrub instance-placement data so decoded class meshes can be placed into world space. The desired next milestone is a neutral OBP world fixture containing retail UYA terrain, collision, decoded textures, TIE/shrub class meshes, and their world transforms.
