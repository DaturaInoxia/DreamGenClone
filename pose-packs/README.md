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
