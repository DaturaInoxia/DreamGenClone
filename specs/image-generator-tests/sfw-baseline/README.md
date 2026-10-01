# SFW Baseline — Everyday Life, Locations and Characters

The **location and character** catalog: ordinary activities, in the real places of one scenario, with one
to three people in frame. Where [`baseline/`](../baseline/README.md) asks *what can this model do with a
position*, this asks *can this model hold a place, and the people in it, without the location drifting or
the person count collapsing*.

Source of the places: the **Campground Intimacy** scenario
(`135a9237-bcc1-45ee-afa1-ff2f14c49e50`) — its `Locations` and its `Setting.EnvironmentalDetails`, both
reworked for image prompts in §1 and §2 below.

## 1. Locations — paired down, then detailed up

The scenario's Locations are **narrative** blocks: sightlines, who can hear what, which door does not
latch. That is the right shape for a language model and the wrong shape for an image prompt, which needs
**materials, surfaces, light direction and depth**. So each location was paired down to what a camera can
see, and the thin `EnvironmentalDetails` one-liners were expanded with the visual specifics they were
missing. Nothing was invented that the scenario contradicts; everything added is visible-in-frame detail.

| # | Catalog location | Paired down from | Detail added (was a one-line EnvironmentalDetail) |
|---|---|---|---|
| 1 | `hiking-trail` | Hiking Trails — Secluded Rest Shelter (the trail) | packed dirt and loose gravel, low ferns and leaf litter, trunks close on both sides, canopy light broken into moving patches, pollen haze late morning, birdsong |
| 2 | `rest-shelter` | Hiking Trails — Secluded Rest Shelter (the structure) | weathered grey timber, moss on the shake shingles, plank bench along the back wall, dried leaves on the floor, cropped grass clearing ringed by thickets |
| 3 | `trailer-living-room` | Husband and Wife Trailer | worn couch and low table, vinyl floor, warped slatted blinds half shut, one strip of hard daylight across the lino, thin walls, folded blanket |
| 4 | `trailer-deck` | Husband and Wife Trailer (kitchen/dining + deck) | chipped laminate counter, dish rack, two mismatched chairs, deck boards cupped by weather, clothes line strung across twenty feet of open grass |
| 5 | `other-trailer` | The Other Man's Trailer | warped blinds, creaking vinyl floor, guitar in the corner, stack of paperbacks, one lamp on, road dust on the lower windows |
| 6 | `camp-bathrooms` | Bathrooms — Older Building | pale green tiles with grey grout, stall partitions with a gap at the bottom, paper towel on the floor, hand dryer, high window light, tile echo |
| 7 | `shower-block` | Private Enclosed Showers — Under Construction | wet concrete board, steam on the mirror, sticky latch, stacked plumbing, strip light, puddles by the drains |
| 8 | `fire-pit` | Environmental Details 3 and 9 (the communal fire pit) | dark evening, orange firelight moving on faces, sparks rising, mosquitoes in the light, cooler and cans on the log, black tree line behind |
| 9 | `activity-area` | Environmental Detail 4 (daytime activity area) | whitewashed pit edges, chalked shuffleboard lane, sagging volleyball net, scuffed grass, midday glare, scoreboard chalked on a board |
| 10 | `lake-beach` | Environmental Detail 5 (the beach) | warm shallow water, buoyed swim area, floating dock, striped towels, wet footprints, paddle against a post, hard summer light |
| 11 | `maintenance-shed` | Environmental Detail 7 (the maintenance shed) | rust patches and streaked panels, gravel apron, coiled hose, gas cans, workbench, deep shadow under the trees, dust in a shaft of light |
| 12 | `flower-garden` | Environmental Detail 8 (the wife's flower garden) | turned dark soil, marigolds and tomato cages, kneeling pad, watering can, mid-afternoon sun, the opposite trailer's windows framed behind |

## 2. The cell table — 12 locations × 3 casts

Every location is rendered three times, once per cast, with a **different activity, time of day and
framing** each time. That is what makes 36 cells worth 36 images: the variable under test is the place and
the people, not the prompt.

| # | Location | Cast | Activity | Time of day |
|---|---|---|---|---|
| 1 | `hiking-trail` | 1F | walking the trail with a daypack, seen from the side | late morning |
| 2 | `hiking-trail` | 1F1M | two walking single file, seen from behind | late morning |
| 3 | `hiking-trail` | 1F2M | three walking single file, seen from behind | late morning |
| 4 | `rest-shelter` | 1F | sitting on the bench, one knee up, water bottle | afternoon |
| 5 | `rest-shelter` | 1F1M | two on the bench sharing a sandwich | afternoon |
| 6 | `rest-shelter` | 1F2M | two sitting on the bench, one standing at the post | afternoon |
| 7 | `trailer-living-room` | 1F | reading on the couch, feet tucked up | morning |
| 8 | `trailer-living-room` | 1F1M | he watches TV, she turns to talk to him | evening |
| 9 | `trailer-living-room` | 1F2M | one on the couch, two on the floor playing cards | evening |
| 10 | `trailer-deck` | 1F | pouring coffee at the counter, looking out the sliding doors | early morning |
| 11 | `trailer-deck` | 1F1M | he leans on the rail, she brings two mugs out | morning |
| 12 | `trailer-deck` | 1F2M | two on the rail, one in the deck chair | late afternoon |
| 13 | `other-trailer` | 1F | standing in the doorway with a laundry basket on her hip | afternoon |
| 14 | `other-trailer` | 1F1M | two in the kitchen, one pouring two drinks | late afternoon |
| 15 | `other-trailer` | 1F2M | one leaning on the counter, two talking behind | evening |
| 16 | `camp-bathrooms` | 1F | at the sinks washing her hands, seen in the long mirror | midday |
| 17 | `camp-bathrooms` | 1F1M | two at the sinks mid-conversation, seen in the mirror | evening |
| 18 | `camp-bathrooms` | 1F2M | she is at the sink, two men behind her in the mirror | midday |
| 19 | `shower-block` | 1F | towel-wrapped, wet hair, hand on a shower room door | morning |
| 20 | `shower-block` | 1F1M | she towels her hair, he waits with a bag | morning |
| 21 | `shower-block` | 1F2M | she sits on the bench in a towel, two men talk at the far sinks | morning |
| 22 | `fire-pit` | 1F | on a log with a mug, firelight on her face | night |
| 23 | `fire-pit` | 1F1M | two on a log with drinks, fire between them and the camera | night |
| 24 | `fire-pit` | 1F2M | three around the fire, she sits between the two men | night |
| 25 | `activity-area` | 1F | swinging a horseshoe, mid-throw | midday |
| 26 | `activity-area` | 1F1M | two at the pit, one throwing one watching | midday |
| 27 | `activity-area` | 1F2M | three at the volleyball net, she is at the net | midday |
| 28 | `lake-beach` | 1F | knee-deep in the water in a swimsuit, looking back | summer afternoon |
| 29 | `lake-beach` | 1F1M | two on striped towels, she leans back against him | afternoon |
| 30 | `lake-beach` | 1F2M | she is waist-deep, the two men on the sand behind | afternoon |
| 31 | `maintenance-shed` | 1F | holding a coil of hose in the shed doorway | late afternoon |
| 32 | `maintenance-shed` | 1F1M | he carries a gas can out, she holds the door | late afternoon |
| 33 | `maintenance-shed` | 1F2M | one with a toolbox, two with a ladder | late afternoon |
| 34 | `flower-garden` | 1F | kneeling on a pad weeding, back to the opposite trailer | afternoon |
| 35 | `flower-garden` | 1F1M | she kneels weeding, he stands over her with the watering can | afternoon |
| 36 | `flower-garden` | 1F2M | she kneels in the bed, the two men stand on the grass behind | afternoon |

**Rating.** 30 cells are `rating_safe`. Six are `rating_questionable` and are the scenario's own beats, not
inventions: the three `shower-block` cells (towel, wet hair) and the three `flower-garden` cells (the
scenario states the weeding "requires lots of bending over and getting on hands and knees"). No cell is
`rating_explicit` — that is what `baseline/` is for.

## 3. Cast

| Key | Meaning | Who, in this scenario |
|---|---|---|
| `1F` | one woman | Becky |
| `1F1M` | one woman + one man | Becky + Ken (or Becky + Dean, where the prompt says so) |
| `1F2M` | one woman + two men | Becky + Ken + Dean |

The prose dialects state the cast **explicitly and by number** in every variant: it is the one instruction
the SDXL research marks as mandatory, and it is what stops three people becoming two.

**One fixed look per character.** The scenario describes who these people *are* and says nothing about how
they look, so this catalog fixes one appearance each and reuses it in all 36 cells:

| Character | Catalog appearance |
|---|---|
| Becky | mid-thirties, shoulder-length wavy auburn hair, lightly curvy, untied or tied back as the activity needs |
| Ken | early forties, short dark hair thinning at the temples, soft build, cap in outdoor cells |
| Dean | early thirties, short black hair, athletic build |

That is deliberate: a catalog that re-invented each face would measure the model's re-interpretation of
the prompt rather than its ability to hold a place, and two cells could not be compared by eye.

## 4. Position file format

```json
{
  "id": "trail-walk-alone",
  "title": "Hiking trail — walking alone",
  "actors": "1F",
  "closeup": false,
  "userInput": "Becky walking the hiking trail by herself",
  "expected": "A photorealistic photograph of one adult woman ...",
  "neutralScene": "One adult woman walking ...",
  "variants": { "biglust": "...", "juggernaut": "...", "pony": "...", "qwen-image-2.1": "...", "flux": "...", "qwen-edit-2511": "...", "qwen-image-2.1-edit": "..." },
  "settings": { "seed": 40101, "steps": 30, "cfg": 5.0, "sampler": "dpmpp_2m_sde", "scheduler": "karras", "denoise": 1.0, "width": 1024, "height": 1024 }
}
```

Notes that matter:

- **No `negative` field.** SDXL-family and FLUX authors specify none, and Pony's short guard set is declared
  on the checkpoint profile — a catalog negative would be dead data (see `baseline/README.md`).
- **No `source` field, no other undocumented property.** `PromptSuiteManifestValidation` reports an unknown
  property as a problem, deliberately and visibly, so new content must not invent fields.
- The manifest lists `id` + `path` only. `actors` and `closeup` live in the position file, which is what the
  parser reads; keeping a second copy in the manifest would only let the two drift apart.
- `settings.seed` is **declared per cell and unique**. A declared-seed run renders each cell reproducibly,
  and a cell that declares no seed is skipped by a declared run rather than silently randomised.

## 5. Model dialects (unchanged from `baseline/`)

| Key | Dialect | Prompt rule |
|---|---|---|
| `biglust`, `juggernaut` | natural language | 2–4 short sentences of photoreal brief prose; gender and number stated explicitly; photoreal cues; no tags; under ~800 chars |
| `pony` | danbooru tags | full V6 quality string first, then `rating_*`, then a count tag, then short character/scene/light tags, then ONE camera tag; under ~800 chars / ~40 tags |
| `qwen-image-2.1` | descriptive prose | appearance, clothing, action, environment, lighting, framing, treatment, all stated explicitly |
| `flux` | ordered fields | subject → action → critical style → context → secondary; attributes attached to their subject; no negative |
| `qwen-edit-2511`, `qwen-image-2.1-edit` | edit instruction | names what changes **and** what is preserved. For these SFW cells the edit is a **restage**: move the same people, in the same place, to a different time of day or viewing angle. |

The two Edit variants are instructions, not prompts: they need a source image, so a run against an edit
model is a two-stage chain per cell (render the base, then edit it).

## 6. Running it

1. Playground → **Catalogs on disk** → Import (the importer finds `sfw-baseline/manifest.json`).
2. Pick the suite, choose a **variant key** (`biglust`, `pony`, …) and a model.
3. Tick cells — or all 36 — and Compose. Every render lands in the Asset Manager as a run container.

## Files

- `manifest.json` — the suite envelope, the model legend, and the 36 cell index
- `positions/*.json` — the 36 cells, seven variants each
