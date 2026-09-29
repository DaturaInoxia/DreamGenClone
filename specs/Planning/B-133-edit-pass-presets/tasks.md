# B-133 — tasks

Legend: `[x]` done, `[ ]` open, `[~]` partially done.

## Phase 1 — preset data (DONE 2026-09-27)

- [x] **B133-001** `ImagePresetKeys` — axis/mode enums, 24 keys, `All`, `ShortName`, `PresetKeyFor` (refuses a
      non-lighting/expression axis by name), `AssemblyKey`, `PreserveKey`, `AxisOf`.
- [x] **B133-002** Seed 24 rows into `ImageWorkflowPromptTemplates`: 2 preserve clauses, 4 assemblies, 6 lighting
      details, 12 expression details (6 aligned with the matrix + `angry`/`sad`/`afraid`/`disgusted`/`crying`/`aroused`).
- [x] **B133-003** `ImagePresetInstructionComposer` — deterministic assembly; refuse an unfilled slot, a missing
      preserve clause in `Change` mode, and a preserve slot in a `Condition` assembly.
- [x] **B133-004** `ImagePresetSeedTests` — every key resolves; details carry no full stop and a length floor;
      lighting details name ≥3 light properties and no setting object; expression details name ≥3 facial action units;
      assemblies carry the slots their mode needs; preserve clauses name face/pose/framing/clothing; every matrix axis
      value has a preset; an axis without presets is refused by name.
- [x] **B133-005** `ImagePresetInstructionComposerTests` — both modes per axis, the three refusals, an unknown slot,
      assembly-key mapping, `AxisOf`, `ShortName`.
- [x] **B133-006** Build + focused tests: solution builds clean; 225 tests green (preset + LoRA suites).

## Phase 2 — the generic action (DONE 2026-09-27)

- [x] **B133-010** Provenance: `MediaEditPresetProvenance` records the picked preset, its instruction checksum and the
      character scope on the derived row, beside the identity-authored contract. No new operation kind, no payload
      change needed: the row IS the queued request's record.
- [x] **B133-011** `IImagePresetService` + `ImagePresetService` resolve a preset key into an instruction (detail +
      preserve clause + assembly from the store, honouring a character override), list the presets per axis, and map a
      matrix axis value to its preset. `ImagePresetServiceTests` (6).
- [x] **B133-012** The queue path: `EnqueuePresetEditAsync` on the asset edit service (request + interface + impl),
      mirroring the identity-authored route. **Asset store only** — the scene store needs a production stage of its
      own; recorded below as B133-012b.
- [x] **B133-013** The writer arm: `SceneAssetMediaEditSubjectWriter.PreparePresetAsync` re-derives the instruction and
      compares checksums, so a queued instruction whose wording changed underneath it refuses instead of rendering.
- [x] **B133-014** UI: a **Presets** tab in the shared `ImageEditWorkspace` (picker grouped by axis, the assembled
      instruction shown before it is sent, a run button), rendered only where the store declares
      `SupportsPresetEdits` — no control that a store would have to refuse.
- [x] **B133-015** Host default: `ImageEditWorkspace.SeedPresetKey`, and the LoRA cell attempt deck offers
      **Edit pass (lighting)** on the selected attempt, opening `/assets/{assetId}/images/{imageId}/edit?preset=…`
      with the cell's own planned lighting preset preselected.
- [x] **B133-016** The free-text path is untouched: a preset never enters the vision compiler, and the Edit tab's
      intent → prepare → revision → run flow is unchanged.
- [x] **B133-017** Run-path tests in `MediaEditImageEditingJobHandlerTests`: the assembled instruction reaches the
      editor byte for byte, a changed preset wording is refused naming the preset, and a row that is not what the
      preset assembles is refused. Build clean; 541 focused tests green.
- [x] **B133-012b** The scene-image twin (production/composition images): done. `SceneImageProductionStage.Preset`
      (value 4, after Finish) is the stage a preset run records; `SceneImageMediaEditSubjectWriter` has a `Preset`
      switch arm whose `PreparePresetAsync` re-derives the instruction from `EditCompilerProvenanceJson`, compares
      SHA-256, refuses on mismatch, and then reuses `RequireSourceAsync` + `BuildAssetReferencesAsync`.
      `SceneImagePresetEditRequest` + `SceneImageService.EnqueuePresetEditAsync` + `ISceneImageService` +
      `SceneImageEditWorkspaceService.SupportsPresetEdits => true` complete the path, so the **Presets** tab renders
      for scene images. Build clean; 667 focused tests green (3 failures are the known pre-existing
      `SdxlSceneImagePromptBuilderTests`).

## Phase 3 — tabs in Edit and Compose

- [x] **B133-020** The Edit side is a tab today: **Presets** in `ImageEditWorkspace` (B133-014).
- [x] **B133-021** The Compose side: `ImageStepComposer` renders one tab per BLUEPRINT SLOT, so a preset tab is a
      slot-independent control block — B-130's own documented shape for slot-independent step operations. Built as a
      **Lighting / Facial expression** block with two axis buttons and one picker, placed after the slot tabs and
      before the prompt box.
- [x] **B133-022** **Resolved by the operator's rule: the host's condition wins, the operator may override, and the
      composer never rewrites the prompt.** The composer only *reports* the pick (`PresetKey` + `PresetKeyChanged` +
      `PresetChoices` + `PresetNote`); the host owns the prompt and recomposes it. In the LoRA cell workspace the
      default is the cell's own planned lighting (null override = "this cell's plan supplies the condition"), and a
      pick replaces the `{Lighting}` slot of the render prompt and the lighting tag of the caption, so the caption
      cannot disagree with the image. There is no append-to-prompt path and therefore no de-duplication hazard.
- [ ] **B133-023** Expression presets only where the framing can carry a face (close/half), refused with the reason
      elsewhere. **Not built**: framing is known to the host's blueprint, not to the composer, so the refusal belongs
      in the host that passes `PresetChoices` in.

## Phase 4 — LoRA wiring

- [ ] **B133-030** Record the preset key on a cell attempt and on the accepted member. **Blocked on the accept path,
      which is not built** (B-123 Phase 1 P1-05; the plan's own handover records accept as unbuilt), so the record has
      nowhere to land yet.
- [ ] **B133-031** Compose the caption from the recorded preset for that axis, not from the plan snapshot.
- [ ] **B133-032** Run the curation gates against the accepted (edited) image.
- [ ] **B133-033** Test: an accepted relit attempt's caption names the relit condition.

## Phase 5 — widen

- [ ] **B133-040** Additional lighting presets (practical lamp, window daylight, direct sun, backlit haze).
- [ ] **B133-041** Additional expression presets as the operator's scene needs require.
- [ ] **B133-042** Operator editing surface for the preset wording (the store rows already support it; the UI does not
      expose them yet).

## Open / to decide

- First proof: no edit pass has been run yet (Phase 0 was deliberately skipped). The first real pass should be
  measured (subject luminance before/after + identity) before Phase 2 is built on top of it.
- The expression preset list is a judgement call — review the seeded wording and add or drop entries before Phase 3.
