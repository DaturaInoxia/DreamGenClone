# B-157 — Plan: how the gaps are filled

This plan maps each verified gap to the exact source fields, the target, and the code location that
fills it. It is a design brief; tasks are not yet broken down.

## Gap-fill map

| # | Gap (verified) | Source fields | Target | Fill point |
|---|---|---|---|---|
| G1 | Still brief has no frame/blocking field | `Moment.CompositionRationale`; `MomentEnrichment.FrozenStateContractJson.characters[].physicalLocation/position/sightline`; `participantSummary[].involvement` (active/observer) | `SceneBlocking` | `SceneBlockingResolver` (new) + `DeterministicMultimodalMediaCompiler.BuildStill` emits a `blocking` member |
| G2 | Role clause labels collide across characters | `typedReferences[].subjectKey` + `SceneBlocking.subjects` | role clause "the person in `<imageN>` on the LEFT; `<imageN+1>` supplies their build…" | `ReferenceRoleClauses.LineFor` / new `FinalImagePromptPlan` |
| G3 | Prose and role clause disconnected | same `SceneBlocking` feeds both | prose names positions, clause names positions | `NaturalLanguageSceneImagePromptBuilder.BuildCanonicalUserPrompt` (seed `blocking` into the pre-processor input) + `FinalImagePromptPlan` |
| G4 | No single assembly + validation | final draft + bindings + `typedReferences` + `blocking` | `FinalImagePromptPlan` (text + reference map + validation) | replaces `AppendToImagePrompt`/`AppendToEditInstruction` call sites in `SceneImagePromptGenerationJobHandler`, `SceneImageRenderingJobHandler`, `SceneAssetGenerationJobHandler`, edit compiler path |
| G5 | Asset Manager needs a no-prose map | slot bindings only | role clause with trivial blocking | `FinalImagePromptPlan` with empty prose + Asset-Manager staging profile |
| G6 | No operator preview | `FinalImagePromptPlan` output | read-only final-prompt + reference-map + blocking, with placement override | `StepPromptPanel` inside `ImageStepComposer` |

## Implementation steps (draft order)

1. **`SceneBlocking` + `SceneBlockingResolver`** (domain/application): the record, the pure resolver,
   and the `StagingProfile`. Unit-test the derivation against the real brief fixtures
   (`frozenState` world positions + `compositionRationale` + camera intent). Fail-fast when inputs are
   missing and the profile cannot place a subject.
2. **Persist `StagingProfile`** in a new `ImageStagingProfiles` table and add a new tab on the
   `/profiles` page ([`ThemeProfiles.razor`](../../../DreamGenClone.Web/Components/Pages/ThemeProfiles.razor))
   to edit it. Defaults are declared in the profile data, never in code.
3. **Emit `blocking` into the Still brief** in `DeterministicMultimodalMediaCompiler.BuildStill`
   (snapshotted, versioned).
4. **`FinalImagePromptPlan`**: assemble prose + role clause + reference map + validation. Port the four
   existing append call sites to it; keep `ReferenceBindingShape.InSendOrder` as the single owner of send
   order.
5. **Rework the role clause** to the subject+position form, keyed on `typedReferences` joined to
   `blocking`. Keep `<imageN>` numbering in send order; keep the N=1 no-tag rule.
6. **Seed the pre-processor** with `blocking` so the scene prose names positions in the same vocabulary
   the role clause uses.
7. **Asset Manager assembler**: run `FinalImagePromptPlan` with empty prose + its staging profile.
8. **Composer preview**: extend `StepPromptPanel` with the final prompt, reference map, blocking, and a
   placement override that re-runs the assembly.
9. **Tests**: resolver derivation, role-clause disambiguation (two same-view faces → two subjects),
   validator (tag count, duplicate position, budget 10 / pose 6), snapshot reproducibility, Asset Manager
   assembler, preview contract. Run the affected suites (RolePlay image tests) and the full suite.

## Resolved decisions (design review, 2026-10-09)

1. **Still camera/lens source — derive from moment data, not video coverage.**
   `DeterministicMultimodalMediaCompiler.BuildStill` already refuses a `VideoCoveragePlanId`
   (`RequireNoCoverage(StillImage)`), and `VideoCoveragePlans` are video semantics. The resolver consumes
   `Moment.CompositionRationale` + `FrozenStateContractJson.characters[]` + `participantSummary`, plus the
   staging profile. Video coverage is not read by the still path.
2. **`StagingProfile` persistence — new `ImageStagingProfiles` table, edited as a new tab on the
   `/profiles` page** ([`ThemeProfiles.razor`](../../../DreamGenClone.Web/Components/Pages/ThemeProfiles.razor)),
   which already hosts the tabbed profile surface (Theme / Intensity / … / Templates). Not Model Manager.
3. **Default placement rule — involvement-based.** Active subject → foreground/center; observers →
   side/background. Declared in the `StagingProfile`, not in code. (Confirmed by operator.)
4. **Blocking derivation — deterministic.** `compositionRationale` carried verbatim into the prose;
   the resolver keyword-extracts only known tokens (`tight/wide/medium/close`, `left/right/from X's side`,
   `low/high`) to fill structured fields; no keyword match → the profile's default camera. No LLM call.
5. **Snapshot vs resolve — snapshot.** `blocking` is written into the brief beside `typedReferences`,
   so a later profile edit cannot rewrite a past render's framing.
6. **Edit path — defer edit-side blocking.** Edits have no compiled brief, so no `SceneBlocking`. The
   edit already disambiguates subjects via the operator's instruction (the 2.1 edit compiler emits
   location-qualified locators). In scope: only the role-clause *label* fix from B-147 S3
   (subject labels, leading-image count).

## Dependencies / related

- `B-130` unified composer (the preview surface), `B-147` edit reference parity (shared send-order +
  role-clause owner), `B-145` location references (named location images already reach the resolver),
  `B-126` multi-character composition (prior art), `B-032` scene image generator (the render path).
