# B-140 — Image provenance, tags and round-trip (spec)

**Created:** 2026-10-02
**State:** design written; Phases 1/4 partially implemented (see tasks.md).

## 1. Problem

Two coupled gaps, one root cause:

1. **An image does not say what made it.** The completed `SceneAssetImage` row carries the prompt, seed, model and
   negative, and (after the 2026-10-02 handler fix) the pose, identity and body ids — but those are **ids**, not what
   an operator reads. Opening an image in the review page cannot answer "which character, which pose, which build,
   which LoRA" without a second lookup. And the id set was, until the fix, incomplete: the completion rewrite was
   dropping the pose/identity/body/LoRA ids entirely.
2. **There is no way to find images by what they contain.** The Asset Manager searches name/id/character/pack only.
   "Every image of Becky, kneeling, NSFW" — the primary use case, *find a reference image* — is not answerable,
   because images have no tags while pose presets do (`Keywords` + structured stance/direction/camera/rating).

The round-trip the operator wants already exists in part — `PromptAssetCreator`'s "load the image" button reads
`ImageGenerationDraft.FromImage` and restores prompt/model/size/negative/seed/bindings — but it does not restore the
pose, the character LoRAs, the (Krea 2) scene LoRAs or the facial-expression/lighting preset, so "re-render exactly"
is not actually exact.

## 2. Decisions

| # | Decision |
|---|---|
| D1 | **Provenance is ids on the image, names at view time.** The image records exactly what the render read, by id. Names are resolved by the view (`GenerationDetails`), never written back: a rename or a superseded row then cannot leave a stale label on an old image. |
| D2 | **Tags are a controlled vocabulary, one owner.** `ImageTagCatalog` reuses the pose-library label owners (`PoseMetadataLabels` etc.) so an image tag and a pose keyword are the same string. Auto-applied at enqueue, and manually addable/removable on the asset page. |
| D3 | **Scene (Krea 2) LoRAs are recorded by file name**, exactly as the Studio already stores them (`SceneImageLoraSelection`), because the file name is what ComfyUI loads and what survives a catalog row re-create. |
| D4 | **Expression/lighting is recorded as the preset key plus its clause**, not parsed out of the prompt. The clause is what actually rendered; the key is what the round-trip reselects. |
| D5 | **The review panel names things; the round-trip restores ids.** Two consumers, one source record. |
| D6 | **Tags are for search; the panel is for exact re-render.** They are not the same field, because search wants a vocabulary and re-render wants exact ids. |

## 3. Functional requirements

### Provenance
- **FR-1** Every generated `SceneAssetImage` records, in its own row, the full input set by id: prompt, compiled
  prompt, compiler, model, size, negative, seed, references, pose (preset or stance), identity pack + face, body
  pack + asset, body angle, character LoRAs, **scene LoRAs**, and the applied **expression/lighting preset**.
- **FR-2** A field the image did not record reads as null, never a default; the review surface omits it rather than
  inventing it. (An upload, or a row written before the field existed, is an honest "not recorded".)
- **FR-3** The review surface shows each recorded input **by name** (character, pack version, face/body angle+state,
  pose name + library, wardrobe/location asset name, LoRA character + trigger + strength), resolving at view time.

### Scene LoRAs on the asset path
- **FR-4** `SceneAssetImageGenerationOptions` carries `SceneLoras` (list of file name + strength), threaded through
  the payload and handler to the graph exactly as the Studio's path does; an unknown file or wrong-family file fails
  the render by name, never silently dropped.
- **FR-5** The suite/playground run surface offers the scene-LoRA picker for the selected model's family, reusing
  `SceneLoraPicker`.

### Tags
- **FR-6** Every image carries a flat tag list (`SceneAssetImage.TagsJson`) drawn from `ImageTagCatalog`: character
  name, stance, direction, camera, rating (sfw/nsfw), sex position (missionary/doggy/…), wardrobe item, location,
  LoRA character, scene-LoRA purpose.
- **FR-7** Tags are auto-applied at enqueue from the recorded inputs and the pose/cell metadata; the operator may
  add or remove tags on the asset page.
- **FR-8** The catalog vocabulary is shared with the pose library's keywords, so images and poses are searchable with
  the same strings.

### Round-trip
- **FR-9** "Load the image" restores, in addition to what it restores today: the pose (as a binding), the character
  LoRAs, the scene LoRAs, and the expression/lighting preset — so a re-render is byte-exact given the same seed and
  model.
- **FR-10** A field the image did not record is left at the panel's current value and reported ("did not record
  X"), never defaulted.

### Search
- **FR-11** Asset Manager search matches tags in addition to name/id/character/pack.
- **FR-12** A "reference images" filter (or the same search scoped to it) makes the primary use case one query.

## 4. Out of scope (stated, not forgotten)

- Storing resolved **names** on the image (D1 — view-time only).
- Backfilling tags for pre-existing images (a one-time backfill can be added later from existing metadata; images
  rendered before the tag column exist with an empty tag list, which is an honest state).
- Tagging the production `ProducedImage` / Review Deck path (different model; same vocabulary can be added later).
