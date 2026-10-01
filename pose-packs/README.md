# Pose packs

One folder per pose pack. Each folder is imported as **one library** in the app's Pose Library, so the
library list is a list of packs plus whatever libraries the operator creates.

```
pose-packs/
  README.md            <- this file
  openpose-nsfw/       <- the bundled pack (the first library)
    pack.json          <- required: the library's name and provenance
    NSFW_standing/...  <- the pack's own folder layout, untouched
```

## `pack.json` is required

A pack folder without a `pack.json` is **refused by name**, it is not guessed at. The importer needs a name
and a provenance for every library it creates, and inventing either from a folder name would turn a guess
into a record.

```json
{
  "name": "OpenPose NSFW pack",
  "description": "One line about what this pack holds.",
  "source": "bundled | https://the-url-it-came-from",
  "attribution": "Who made it and where it came from.",
  "license": "the pack's licence, or 'unverified'"
}
```

`name` is required. Everything else is optional and defaults to an empty string, so a missing field is a
documented absence rather than a fabricated value.

## What the poses ARE: `rating` and `categories`

Keypoints say where the joints are. They do not say what the pose is DOING, which way it faces, where the camera
sits, or whether the subject is clothed — a lying body photographed from above and a standing body photographed
from the front produce almost identical 2D skeletons (measured: torso-vertical 0.86 vs 0.92). So each pack
DECLARES it, and the app stores the declaration:

```json
{
  "name": "OpenPose NSFW pack",
  "rating": "nsfw",
  "categories": {
    "NSFW_standing": { "stance": "standing", "direction": "front", "camera": "eye-level" },
    "NSFW_lying":    { "stance": "lying",    "direction": "front" },
    "metalstocks":   { "stance": "sitting",  "direction": "front", "camera": "eye-level" }
  },
  "poses": {
    "NSFW_sitting/512768/NSFW_sitting014.json": { "direction": "profile-left" }
  }
}
```

- **`rating`** — `nsfw` or `sfw`, the pack's default. It chooses the prompt's subject (`a naked woman` /
  `a woman, fully clothed`) AND which body reference the test conditions on (unclothed / clothed). A pack that
  declares no rating gets no prompt at all: the app will not decide what a pack contains.
- **`categories`** — one entry per category FOLDER, or per category NAME. A key may be either, and the deeper
  path wins: `openpose-nsfw` holds `NSFW_lying/` AND `lying/`, which the importer files under the single category
  `lying`, so only path keys can tell those 38 poses from the other 35.
- **`poses`** — per-file entries, keyed by the path relative to the pack root, for a pose that differs from its
  folder. Only the fields it states are overridden.
- Fields: `stance` (standing, sitting, kneeling, lying, all-fours, squatting, suspended, split-legs, jumping,
  dancing, flexing, t-pose), `direction` (front, three-quarter-left, three-quarter-right, profile-left,
  profile-right, back), `camera` (eye-level, from-above, from-below), `rating` (sfw, nsfw). Case, spaces and
  hyphens are ignored, so `Three Quarter Left` and `three-quarter-left` are the same value.
- **An unrecognised word is an ERROR**, not a default: `"stnading"` stops the import with the pack, the key and
  the offending text named. A typo silently becoming "not declared" would send the operator looking in the wrong
  place.
- **Nothing declared is not an error.** A pack with no declaration block imports normally, its poses say
  "not declared", and the Pose Library page names the categories that need a declaration.

The declaration decides; the app's own measurement of the keypoints only CONTRADICTS it out loud. A pose whose
shoulders read the opposite way to its declared direction is flagged for review (the library card says so), and
the declared value is still what the render uses — the flag is a request to look, not a silent correction.

## Rules the importer follows

- **Idempotent.** Re-importing a pack adds no rows and leaves metadata you have edited alone. Only a missing
  skeleton image is regenerated.
- **One person per file.** A pack file holding two people is skipped with its reason rather than truncated.
- **Skeletons are derived.** The app renders its own conditioning PNGs from the pack's keypoints into
  `wwwroot/pose-library/library/<pack>/`, which is git-ignored. Nothing depends on the pack shipping PNGs.
- **`known-good` is never taken from a manifest.** It records a measurement made on this stack, so a pack
  cannot claim it. The three verified stances are held in code as recorded evidence.
- **Downloaded packs** land in a new folder here, get a `pack.json` naming the URL they came from, and are
  never extracted over an existing pack.

## Adding a pack

- **Bundled/local:** copy the folder in and add `pack.json`. Re-import from the Pose Library page.
- **Downloaded:** use *Download pack* on the Pose Library page with the pack's zip URL and a name. It is
  refused if the pack already exists, if the download is not a zip, or if any entry tries to escape the pack
  folder.
