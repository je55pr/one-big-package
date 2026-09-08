# UYA retail multi-row WAD-LZ / collision compatibility census

Authority build: `rac3-ntscu-original` (`SCUS-97353`, NTSC-U original retail).

Purpose: test whether the successful row-1 result in `UYA_RETAIL_ROW1_WORLD_COMPATIBILITY.md` was an isolated compatible block or evidence of a broader UYA format family.

All reads were bounded HTTP ranges against the user's shared retail split set via GitLab CI. Share URLs and Drive IDs are transport-only and are not committed.

## Sample design

Four distinct normal table rows were tested with the exact same code paths and no parser relaxation:

| Row | Candidate payload location | Transport purpose | Pipeline iid | UYA range job |
|---:|---|---|---:|---:|
| 1 | wholly in part 5 | initial representative | 265 | 16348660664 |
| 8 | begins in part 5 and crosses into part 6 | mapped cross-chunk read | 271 | 16348891667 |
| 20 | wholly in part 6 | independent middle-disc payload | 272 | 16348895026 |
| 50 | wholly in part 7 | independent later-disc payload | 273 | 16348898315 |

Pipelines 265, 271, 272 and 273 all passed. Every run first matched the exact retail candidate-ToC authority window SHA-256 `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`.

Row 8 is particularly useful transport evidence because its candidate main extent spans a physical split boundary; the logical mapped reader transparently read the required bounded ranges from parts 5 and 6 without reconstructing the ISO.

## CoreData / WAD-LZ results

Every sampled row passed all strict compatibility checks:

- public core compressed size == WAD header compressed size;
- exact compressed block size == declared compressed size;
- unchanged OBP WAD-LZ decoder succeeded;
- public decompressed size == actual decoder output size;
- no UYA-specific fallback.

| Row | coreIndex bytes | coreIndex SHA-256 | compressed coreData bytes | compressed SHA-256 | decoded bytes | decoded SHA-256 |
|---:|---:|---|---:|---|---:|---|
| 1 | 35,216 | `8e9b46845270a7678b473b621bf66fde0d752f7b0d921ace897a85981016de2c` | 12,166,965 | `c17108438e245bd4db3342da5aace39e2d030bd35a062a3f3a81e5817f9eef39` | 20,000,480 | `d8f6ebde468c115a9b6bf48d3c5723f03cea552cae192447dba919c426acd7f7` |
| 8 | 27,792 | `47d685339ec125e7246b0e5aeb6ffe64c22b380fdce6b6d686c70e8d7a33ad15` | 9,571,910 | `e86a32b1fc7da3b774e7ccc0c94603037a35fae421346cae4e94ad811a00b546` | 16,375,888 | `a80563feb7998d26738c45afe7ac4f3886b0c932c534034cd3682d1678b2e481` |
| 20 | 32,112 | `a93d350250144504a8c2449fc9b7eac46b927c387270446ed675eb1564f97c66` | 11,907,803 | `423a333f3076aa535cc5fcbdfc29949b2c96d5b1a686ef4670a521899c405653` | 20,511,872 | `49e5d42c3187c7bd479da027cd662cbfd7f38babc496129eac93466449dee7bd` |
| 50 | 27,008 | `e2195645631dd98be086374c586081932cce4e4731a234f5fe9d71dbffb46177` | 10,268,247 | `98498108796a6a3875e9b038c5c97ec7dfdefc1d44cf8db2318b4207bacb571b` | 17,555,520 | `05215d3068e5fd924c601ca462ad27b59791950e1e2fb36bb381ed3d75e2a4b8` |

The four blocks are demonstrably distinct by size and SHA-256.

### Promotion

It is now reasonable to classify **OBP WAD-LZ as retail-confirmed compatible across multiple sampled UYA level coreData blocks**, not merely one exact row.

This is strong format-family evidence, though it is not an exhaustive proof that every compressed block anywhere on the disc uses the same variant.

## Collision results

For each decoded coreData block, the candidate collision extent was derived using the same pinned-public core boundary rules and then passed unchanged to the existing GC-verified `readRcCollision()` parser.

All four distinct candidate blocks were accepted without relaxed validation or a UYA-specific collision reader.

| Row | Candidate collision offset | Bytes | SHA-256 | Octants | Vertices | Triangles | Hero groups |
|---:|---:|---:|---|---:|---:|---:|---:|
| 1 | 3,216,960 | 2,471,488 | `0bcf82e12b70a35010f5315e07f169ca54fd8ff508268d2a3c9ffe7159ed1ef5` | 21,510 | 306,015 | 264,313 | 51 |
| 8 | 4,653,248 | 1,045,184 | `a6ef51034bc663a580df1cf081f9bb14b078e43c8d8fa04760d8192bd60adcbf` | 9,750 | 133,922 | 112,813 | 14 |
| 20 | 2,699,584 | 2,126,272 | `bb72cc350c168231d72cef982b0022ef789ef5f2449a45157e157c7848af9775` | 20,835 | 257,071 | 236,309 | 5 |
| 50 | 2,857,792 | 2,472,256 | `7bc895487234ce837c0c34374216ffff28be6494a7d5c1bedfb5524452c7a973` | 34,049 | 289,862 | 222,264 | 16 |

Representative native material sets differ between levels as expected:

- row 1: `9,10,12,31,41,44,63,95,127,159`
- row 8: `0,9,12,14,31,41,44,63,95,140,159`
- row 20: `9,11,12,31,63,95,127`
- row 50: `1,2,9,11,12,31,63,95,108,127,159`

Distinct sizes, hashes, mesh counts, material sets, bounds and hero-group counts show that parser success is not caused by repeatedly reading the same data.

### Promotion

It is now reasonable to classify **the existing GC collision binary parser as retail-confirmed compatible across multiple sampled UYA candidate collision blocks**.

The semantic caveat remains separate: the collision extents are selected using public-derived 0xbc core field/boundary semantics. Multiple structurally valid collision meshes make that semantic interpretation substantially stronger, but independent native executable/decompilation evidence is still preferred before declaring the field name/provenance fully native-confirmed.

## Cross-chunk transport result

Row 8's main payload starts in part 5 and extends into part 6. Pipeline iid 271 successfully completed the entire fixed-header, decompression and collision path with parts 1, 5 and 6 mapped at their real logical offsets.

Therefore the sparse HTTP mapped reader is not limited to payloads contained in one Drive object; bounded reads can cross the original split boundary while preserving a single logical disc address space.

## Current evidence boundary

Promoted by this four-row census:

- WAD-LZ compatibility is **multi-row retail-confirmed** for UYA level coreData samples.
- GC collision parser compatibility is **multi-row retail-confirmed** for UYA candidate collision samples.
- 0x60/0x58/0xbc structural interpretation has repeated retail corroboration across the sampled rows.
- sparse HTTP split transport works for both single-chunk and cross-chunk payloads.

Still not independently native-confirmed:

- LBA-1001 loader provenance;
- ToC slot semantic names;
- native names for individual 0x60/0x58/0xbc fields;
- the candidate collision field's executable provenance;
- untested low-level families such as tfrags/VIF, textures, TIE, shrub and Moby.

## Next useful work

Additional collision rows now have diminishing value compared with opening the next asset family. The next strong experiment should reuse these retail-confirmed coreData blocks to test a game-aware or less-certain system such as tfrags/textures, while executable archaeology continues independently around the LBA-1001 candidate at `0x0013d2ec`.
