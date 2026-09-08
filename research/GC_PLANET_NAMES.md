# Going Commando planet / location names

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

The [`GC_LEVEL_CATALOGUE.md`](GC_LEVEL_CATALOGUE.md) notes said the planet-name
table "has not been located yet". It is **not in the executable** — the boot ELF
(`SCUS_972.68`) holds only the loading-screen format string and some index
tables. The names themselves live in the **global localised string bank**.

## Where the names are

### 1. The global string bank (`gameplay` lump, help-messages block)

Every level's `gameplay` lump (level-WAD slot 2, WAD-LZ) has, at pointer table
offset `0x10`, a `HelpMessageBlock` (Wrench's name) — `~2181` strings. It is the
**whole game's UI text** and is byte-identical in every level. Format:

```text
HelpMessageHeader @ 0   { s32 count; s32 size }
HelpMessageEntry[count] @ 8  (0x10 each)
  0x00 s32 offset            string at blockStart + offset (0 => no string)
  0x04 s16 id                the lookup id
  0x06 s16 short_id
  0x08 s16 third_person_id
  0x0a s16 coop_id
  0x0c s16 vag               voice line
  0x0e s16 character
strings: NUL-terminated, `\x01`/`\x0e`/`\x14` etc. are in-band formatting codes
```

Pointers `0x10`–`0x2c` are the per-language copies (US/UK english, french,
german, spanish, italian, japanese, korean).

**Planet names** — a contiguous run at **help ids `2885`–`2910`**:

| idx | id | name | | idx | id | name |
|--:|--:|---|---|--:|--:|---|
| 0 | 2885 | Aranos          | | 11 | 2896 | Joba |
| 1 | 2886 | Oozla           | | 12 | 2897 | Todano |
| 2 | 2887 | Maktar Nebula   | | 13 | 2898 | Boldan |
| 3 | 2888 | Endako          | | 14 | 2899 | Aranos *(return)* |
| 4 | 2889 | Barlow          | | 15 | 2900 | Gorn |
| 5 | 2890 | Feltzin System  | | 16 | 2901 | Snivelak |
| 6 | 2891 | Notak           | | 17 | 2902 | Smolg |
| 7 | 2892 | Siberius        | | 18 | 2903 | Damosel |
| 8 | 2893 | Tabora          | | 19 | 2904 | Grelbin |
| 9 | 2894 | Dobbo           | | 20 | 2905 | Yeedil |
| 10 | 2895 | Hrugis Cloud   | | — | 2906 | Dantopia *(unused)* |

(then 2907 "Dobbo Orbit", 2908 "Damosel Orbit", 2909 "Ship Shack",
10011 "Wupash Nebula", 2910 "Cerebella".)

**"Dantopia" (id 2906) is a cut planet.** It occupies the planet-name slot right
after Yeedil (the final planet) but nothing in the shipped game references it —
no level WAD, no "Downloaded coordinates" infobot string, no menu entry, no
description, and no occurrence in the executable. It is also absent from the
UK-English and Japanese string blocks (present in US-English, French, German,
Spanish, Italian). It is **not** the Insomniac Museum: "Insomniac Museum" is a
separate string (id 2880) in the *area-name* list, and the Museum is a real
shipping bonus level with a full menu flow (ids 10240 / 10242 "Go to Insomniac
Museum", id 7085 welcome text, and exhibit blurbs about content cut from the
game — e.g. a car cut from the Snivelak Giant Robot fight, a gadget cut from
R&C1). The Museum does not use a planet name.

The other non-planet level WADs (viewer-checked): `LEVEL24` = **Slim Cognito's
Ship Shack** (the vendor asteroid in a starfield); `LEVEL25` = a starfield +
asteroid scene stub; `LEVEL22 / 23 / 26` = the `is_spherical_world` ship-combat
arenas (Feltzin System / Hrugis Cloud / Gorn); `LEVEL21` (id 30) = the
Aranos-return.

**Location + planet pairs** — help ids **`4592`–`4611`**, the infobot
"Downloaded coordinates for: `<location>`, Planet `<planet>`" strings, in story
order:

| # | location | planet |
|--:|---|---|
| 0 | The Megacorp Outlet | Oozla |
| 1 | Maktar Resort | Maktar Nebula |
| 2 | Megapolis | Endako |
| 3 | Vukovar Canyon | Barlow |
| 4 | Thug Rendezvous | Feltzin System |
| 5 | Canal City | Notak |
| 6 | Frozen Lab | Siberius |
| 7 | Mining Area | Tabora |
| 8 | Testing Facility | Dobbo |
| 9 | Deep Space Disposal | Hrugis Cloud |
| 10 | Megacorp Games | Joba |
| 11 | Megacorp Armory | Todano |
| 12 | **Silver City** | **Boldan** |
| 13 | Floating Prison | Aranos |
| 14 | Thug Fleet | Gorn |
| 15 | Thug Headquarters | Snivelak |
| 16 | Distribution Center | Smolg |
| 17 | Allgon City | Damosel |
| 18 | Tundor Wastes | Grelbin |
| 19 | Protopet Factory | Yeedil |

> **Silver City is on Planet Boldan**, not Endako (whose city is *Megapolis*)
> and not Notak (*Canal City*).

### 2. The executable (`SCUS_972.68`)

Two `PT_LOAD` segments: `0x100080` @ file `0x1000` (`0x251f40`), `0x1800000` @
`0x253000` (`0x15b63`). Relevant finds:

| item | VA | note |
|---|---|---|
| `"%s, Planet %s"` | `0x1ab900` | loading-screen format (also `"%s,  Planet %s"` at `0x1ab9c8`) |
| coordinate-id list | `0x26cb34` | 20 × s32 = `{4592 … 4611}` — the ids above, in order |
| special-level → planet-index | `0x2403c0` | pairs `(level_id, planet_name_index)` for the bonus / moon / space-battle sub-levels, terminated by `-2,-2` |

The special-level table (`0x2403c0`), `level_id` → planet-name index (0..20):

```
22→1  23→3  24→4  26→1  27→9  28→11  29→8  30→0
31→6  32→11 41→3  42→0  43→14 45→14  37→8  25→12
```

e.g. `level_id 30` (`LEVEL21.WAD`, the Aranos-return) → planet 0 = *Aranos*.
The main planet levels (`level_id` 0..21) are not in this table; their mapping is
still being pinned (see below).

## Level file → planet

**Working rule: `planet_name_index == level_id`** for the main levels
(`LEVEL0`–`LEVEL20`, which have `id == file number`), with the ELF special
table (`0x2403c0`) overriding for `id ≥ 22`. The main `level_id →
planet_name_index` table itself has not been found in the ELF (candidates:
`RC2.HDR`, an overlay) — but every level checked in the OBP viewer matches the
rule:

| file/id | planet (= name index) | location | viewer check |
|--:|---|---|---|
| 0  | Aranos          | Floating Prison   | ✅ the camouflaged prison airship, 4 engine pods |
| 1  | Oozla           | The Megacorp Outlet | ✅ the swamp |
| 2  | Maktar Nebula   | Maktar Resort     | ✅ ring-shaped space station, starfield, no ground |
| 3  | Endako          | Megapolis         | ✅ dense vertical city on a cloud deck |
| 4  | Barlow          | Vukovar Canyon    | rock spires + cloud deck (plausible) |
| 6  | Notak           | Canal City        | overcast blue-green city (plausible) |
| 7  | Siberius        | Frozen Lab        | pale grey rock cones, winding path (plausible) |
| 8  | Tabora          | Mining Area       | ✅ small hot-sand desert, rock spikes |
| 9  | Dobbo           | Testing Facility  | dark stormy, rock spires + facility (plausible) |
| 13 | Boldan          | **Silver City**   | tall slender towers, warm sunset sky (plausible) |
| 16 | Snivelak        | Thug Headquarters | ✅ red-dust canyon, dark structure |
| 18 | Damosel         | Allgon City       | ✅ onion-dome + spire architecture, blue canal water |
| 19 | Grelbin         | Tundor Wastes     | ✅ snow peaks + ice spires, night sky |
| 20 | Yeedil          | Protopet Factory  | industrial buildings, storm + starfield (plausible) |

So **`LEVEL3` is Endako (Megapolis)** — not "Silver City". Silver City is
**Boldan = `LEVEL13`**. (`id 14` = planet-name index 14 = a second "Aranos"
string; the actual Aranos-return level is `LEVEL21.WAD`, `id 30`, which the
special table maps to planet 0 — the `id 14` case is unresolved.)

## Policy note

Place names and string ids are recorded as factual field values, per the
research-notes exception. No executable, string bank, or WAD is committed.
