# B-140 — Image provenance, tags and round-trip (plan)

**Created:** 2026-10-02
**Companion:** `spec.md` (requirements + decisions), `tasks.md` (ordered work).

## 1. Architecture

One source record, two consumers, one vocabulary:

```
enqueue / completion                      view time
───────────────────────────────          ─────────────────────────
SceneAssetImage.AssociationMetadataJson  SceneAssetImageGenerationDetails  ──> GenerationDetails.razor (names)
  (ids: refs, pose, identity, body,      ImageGenerationDraft               ──> PromptAssetCreator.ApplyDraftAsync (exact re-render)
   characterLoras, sceneLoras, presets)
SceneAssetImage.TagsJson                 ImageTagCatalog (vocabulary)       ──> Asset search / reference filter
```

- **`AssociationMetadataJson`** is the machine record, already written by `SceneAssetGenerationJobHandler` at
  completion. Phase 1 extends it (scene LoRAs, presets) so it carries the complete id set.
- **`SceneAssetImageGenerationDetails`** (done) is the read-back used by the review panel, resolving ids to names at
  view time (D1). It is extended in Phase 1 to read the two new fields.
- **`ImageGenerationDraft`** is the round-trip read-back; Phase 3 extends it to restore pose, both LoRA kinds and the
  preset.
- **`TagsJson`** is a new column written at enqueue (Phase 2) from the same inputs, using `ImageTagCatalog`.

## 2. Key changes by phase

### Phase 1 — complete the machine record (mostly done)

Already done this session:
- `SceneAssetGenerationJobHandler` now records `posePresetId`, `poseSkeletonRelativePath`, `poseStance`,
  `poseStrength`, `identityPackId`, `identityFaceAssetId`, `bodyReferencePackId`, `bodyReferenceAssetId`,
  `bodyAngleView`, `bodyAngleSourceImageId`, `characterLoras` into the completed `AssociationMetadataJson`.
- `SceneAssetImageGenerationDetails.FromImage` reads them; `GenerationDetails.razor` renders them by name on the
  asset review page.

Remaining:
1. **Scene (Krea 2) LoRAs on the asset path.** `SceneAssetImageGenerationOptions` gains `SceneLoras`
   (`List<SceneImageLoraSelection>`, file-name keyed, mirroring `SceneImageStudioSettings.SceneLoras`); it is
   threaded into `SceneAssetGenerationJobPayload`, resolved by the existing `SceneLoraResolver` in the handler and
   applied to the graph the same way the Studio does; the selection is recorded in the metadata and shown by the
   review panel. The Playground/catalog run surface offers `SceneLoraPicker` for the selected model's family.
2. **Expression/lighting preset.** The payload/options gain an applied-preset record (`AppliedPresetsJson`:
   axis → preset key + clause). `PromptAssetCreator` writes it when a preset is applied; the handler records it; the
   review panel shows the key + clause (D4).

### Phase 2 — tags

- New `ImageTagCatalog` (static vocabulary owner) with axes: character, stance, direction, camera, rating, sex
  position, wardrobe, location, LoRA character, scene-LoRA purpose. It reuses `PoseMetadataLabels` for the pose axes
  so image tags and pose keywords are the same strings.
- New `SceneAssetImage.TagsJson` column (ALTER-guarded, idempotent, as every other optional column).
- New `RenderTagBuilder`: input = payload + options + the cell/position context; output = the tag list, written at
  enqueue in `AddGeneratedImageAsync` (so the tags are recorded even if the render later fails).
  - catalog cell → sex position (from the cell/position id or title), actors count, rating (from variant/closeup
    context), plus any character/pose/wardrobe/location the cell's references carry.
  - pose cell → stance / direction / camera / rating from the pose's own metadata (already resolved by the suite
    driver), plus the character and LoRA tags.
- Asset page: read-only tag chips + add/remove controls.

### Phase 3 — exact round-trip

- Extend `ImageGenerationDraft.FromImage` to read `posePresetId`, `characterLoras`, `sceneLoras`, `AppliedPresetsJson`.
- Extend `PromptAssetCreator.ApplyDraftAsync` to reselect: the pose (as a binding of source `PoseLibrarySkeleton`),
  `_characterLoras`, `_sceneLoras`, and the applied preset(s).
- Add a "Load into composer" action on the asset review page that seeds the Generate panel from the same draft, so
  "re-render exactly" is one click.

### Phase 4 — search

- `AssetStudio` search also matches `TagsJson`; each matching tag is shown as a chip.
- Add a "reference images" filter (scope the asset search to reference-capable images: faces, bodies, wardrobe,
  poses, location) so the primary use case — find a reference image — is one query.

## 3. Cross-cutting rules (non-negotiable, per repo policy)

- **No defaults.** A missing recorded field is null and omitted/refused; never a guessed value.
- **One owner per fact.** The tag vocabulary, the pose→angle resolution, the refusal wording and the reference
  resolver each stay single-owned; this plan adds consumers, not second copies.
- **Schema changes are idempotent.** New columns via `pragma_table_info`-guarded ALTERs, matching the existing
  repository convention.
- **All tests green at each phase.** Each phase adds tests before it is declared done.

## 4. Data model deltas

| Table | Column | Notes |
|---|---|---|
| `SceneAssetImages` | `TagsJson TEXT NULL` | flat list of catalog strings |
| (metadata) | `sceneLoras` | array of `{fileName, strength}` inside `AssociationMetadataJson` |
| (metadata) | `appliedPresetsJson` | array of `{axis, key, clause}` inside `AssociationMetadataJson` |

No new tables: tags ride the image row; provenance stays in the existing metadata column; the vocabulary is a
static catalog, not a store (pose presets already carry the same strings in `Keywords` + structured columns).
