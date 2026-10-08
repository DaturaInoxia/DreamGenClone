# B-155 - Video composition drafts + composition library: implementation handoff

**Status:** `planned` - ready to implement. No code has been changed for this item.
**Created:** 2026-10-07
**Depends on:** B-153 (render path, `implemented`), B-152 (scoping / decision record), B-154 (shared background-work refresh, `new` - soft dependency, see V-11).
**Extends:** B-152 **D-3** (the record-only, queue-only composition model) and **D-4** (ordered references).
**Supersedes nothing.** `specs/Planning/B-152-scene-video-composer/SCOPING.md` stays the decision log for the render path; this document is the task list for drafts and the composition library.

---

## 0. How to use this document

1. Read section 2 (verified current state) before writing code. Every claim there has a file/line or a DB/ComfyUI command behind it.
2. Section 3 is the locked decision set. Do not re-litigate; if something here conflicts with the code, follow this document and raise the conflict.
3. Implement in the slice order in section 8. Slices D1 and D2 are the foundation; D5 needs the `Draft` status from D1 and the promote action from D2. The poster enabler inside D5 is independent and can be built first if visible progress is wanted.
4. Section 9 is not advisory: this work touches RP-engine files, so the project's non-negotiables apply in full.
5. Section 10 is the evidence protocol to reproduce before declaring anything done.

---

## 1. Why this exists

Two operator requirements, both dated 2026-10-07:

- **"The user should not have to enter 300-500 words" before queueing.** Already fixed - see section 2.5 (advisory severity split, landed). This document does not revisit it except to note the one remaining compliance gap (the word band is still a page literal, slice D1b).
- **"Can the user start entering data, save it, come back later?"** and **"there needs to be a new UI that can list them all - the drafts and saved completed ones."** This is the whole of B-155: compositions become a saved, resumable document, and there is one surface that lists every composition in every state.

---

## 2. Verified current state

### 2.1 What is persisted today, and when

A composition is written to `SceneVideos` **only at enqueue** ("Stage for later" / "Start now"), by `SceneVideoService.EnqueueAsync` - see `DreamGenClone.Web/Application/RolePlay/SceneVideoService.cs:198`. Its own guards make that exclusive: a non-empty `PromptSnapshot`, at least one reference, and positive length/width/height/steps are all required (`:205`, `:212`, `:219`).

Everything the operator typed before that moment lives only in the page's in-memory lists: `_record`, `_references`, `_shots`, `_dialogue`, `_onScreenText`, `_loraSelections` (`DreamGenClone.Web/Components/Pages/VideoStudio.razor:801` ff.). Refresh or navigate away and it is gone.

### 2.2 What a saved record actually contains (the important finding)

`SceneVideoRecord` (`DreamGenClone.Domain/RolePlay/SceneVideoRecord.cs:90`) is a **render-provenance record, not a composition document**. It has **no shots, dialogue or on-screen-text members at all** - confirmed against the full member list and against the `SceneVideos` DDL in `DreamGenClone.Infrastructure/RolePlay/SceneVideoRepository.cs:23`.

Written at enqueue by the page (`VideoStudio.razor:1075` ff.):

| Persisted | Where |
|---|---|
| title, origin (`OriginKind` / `OriginImageId` / `OriginCoveragePlanId`), session + interaction | columns |
| compiled prompt, manual-edit flag, compiler key + version | `PromptSnapshot`, `PromptManuallyEdited`, `CompilerKey`, `CompilerVersion` |
| style, scene description, soundscape, music, register, canvas **only** | `SettingsJson` (page-authored literal at `:1097`) |
| ordered references | `ReferencesJson` |
| LoRA stack (file name, strength, purpose, trigger token) | `LoraStackJson` |
| model, provider, seed, width, height, length, steps, fps, ref image size | columns |
| job id, status, timestamps | columns |
| stream presence, measured LUFS, measured duration, verification notes | columns |

**Lost on save:** shots (number, cut time, description, camera motion/amplitude/speed), dialogue lines (speaker, identity info, language, verbatim content, delivery, off-screen, voiceover, continuity), on-screen text entries, `allowUntrainedLength`. After queueing these survive **only as prose inside `PromptSnapshot`**. Consequently a queued composition cannot be reopened for editing today even in principle - the structured inputs no longer exist anywhere.

There is also **no `Update`-shaped method** on `ISceneVideoRepository` (`DreamGenClone.Application/RolePlay/ISceneVideoRepository.cs`): `InsertAsync`, `GetAsync`, `TryClaimAsync`, `TryCompleteAsync`, `TryFailAsync`, `TryCancelAsync`, `ListRecentAsync`, `ListBySessionAsync`, `DeleteAsync`. The claim/complete discipline exists for the render lifecycle only.

### 2.3 How a composition is retrieved today

Only the artifact, never the composition:

- **Recent compositions** (`VideoStudio.razor:742` ff.) lists the ten newest records with Title / Status / Frames / Created and a single action: **Open** the mp4 in a new tab. There is no "reopen the composition", no filter, no paging, no drafts.
- The **Queue tab** shows this record's job (status, attempts, lease, error) and plays the output.
- `ISceneVideoService.GetAsync` exists but **nothing in the UI calls it**.
- Otherwise: raw SQL.

### 2.4 Defects found while investigating (fold these in, they are cheap)

| # | Defect | Evidence | Fix |
|---|---|---|---|
| E-1 | The Queue tab's "this composition has not been queued yet" notice is **dead code**. The check is `_record.Id == Guid.Empty.ToString("N")`, but `SceneVideoRecord.Id` self-initialises to `Guid.NewGuid().ToString()`, so it is never a `Guid.Empty` *and* the format differs (dashed vs `"N"`). | `VideoStudio.razor:646`, `SceneVideoRecord.cs:92` | Key the branch off `Status` / `JobId`, not `Id` |
| E-2 | `/roleplay/video-studio/coverage/{CoveragePlanId}` has **no entry point anywhere in the UI** - the route exists, nothing links to it. | the other four routes are linked from `SceneImageStudio`, `CompositionComposer`, `SceneImageGallery`, `AssetStudioView`, `ReviewDeck`; the coverage route is referenced nowhere | Add the beat-pipeline link (D4) |
| E-3 | There is **no nav entry for video at all** - the Video Composer is reachable only from image surfaces. | `DreamGenClone.Web/Components/Layout/NavMenu.razor` | Add the **Video** nav group (D5) |
| E-4 | The word band is a **page literal** (`private int _minWords => 350; private int _maxWords => 500;`), which is a code-only behaviour default in a UI file, against the no-fallback / no-code-only-defaults rule. | `VideoStudio.razor:845` | Move it to the model's capability qualification and read it fail-fast (D1b) |

### 2.5 Live worked example (the only real record today)

The asset composition the operator queued while this plan was written - useful as the regression fixture for resume/retrieval:

| Field | Value |
|---|---|
| Asset | `/roleplay/video-studio/asset/e4fbbf77bb42428ba21736749658153e` |
| Video record | `11980a46-afa7-4f05-966b-6074fb565847` (dashed GUID from the member initialiser) |
| Origin | `OriginKind = AssetImage`, `OriginImageId = e4fbbf77...` |
| Job | `32eb98eeaf2c4c6c94e7fd63825bfa2e`, lane `VideoRender`, status `Processing`, attempt 1/1 |
| Shape | 124 frames @ 24 fps (5.2 s), 1344x768, 40 steps, seed 1, `minimax_h3_ref2va_pruned_w4a8_mixed.safetensors` on `Local ComfyUI (WOOD-GAME-MAIN 5080)` |
| Provenance | `CompilerKey = minimax-h3-ref2va-six-section`, `CompilerVersion = 1.1.0` |
| Notable | `Title` is **empty** (never typed) and `PromptChars` is **999** - the short draft that the advisory-severity split (landed this session) allowed through |

**Advisory severity is already landed and must not regress.** `SceneVideoValidationFinding` carries `IsAdvisory` / `BlocksQueueing`; `SceneVideoCompilationResult.IsValid` means "no blocking failure" and exposes `BlockingFailures` / `Advisories` (`DreamGenClone.Domain/RolePlay/SceneVideoCompilation.cs:139` ff.). Blocking = R01-R09, R12-R16, R18-R20, R25-R30; advisory = R10, R11, R17, R21-R23, R31-R33. An unauthored soundscape or score is emitted as the `N/A` sentinel with an advisory. Compiler version is `1.1.0`.

---

## 3. Locked decisions

| # | Decision | Locked as | Why |
|---|---|---|---|
| **V-1** | Compositions become a saved, resumable document | `SceneVideoStatus.Draft` (appended) + a versioned `ComposerStateJson` on the same record | The record has no shots/dialogue columns, so resume is impossible without it (2.2) |
| **V-2** | Resume behaviour on an origin route | **Resume that origin's newest draft**, with an explicit **New composition** button to start fresh | Operator decision 2026-10-07. Least surprising for "come back later"; the explicit action covers "I want a fresh one" |
| **V-3** | What `/composition/{id}` opens | **Any** record by id. Editable when `Draft`; **read-only with a named reason** for every other status | One route for "show me this composition"; queued/finished rows are history, not editable state |
| **V-4** | One composition, one row, one id, for life | `EnqueueAsync` **promotes the same row** `Draft -> Pending` and mints the job in one transaction | No duplicate rows, no id churn, and the draft URL keeps working after queueing |
| **V-5** | Multiple compositions per origin are allowed | No unique index. "New composition" always creates another draft | A source image may legitimately produce several clips (different length / audio / model) |
| **V-6** | A draft is legal while incomplete | The draft-save path has **no** prompt/reference/length guards. `EnqueueAsync` keeps all of them | Half-typed is the point of a draft; queueing still cannot submit an unfinished graph |
| **V-7** | Exactly one source per value | `ComposerStateJson` carries **only** what has no column today. It must not duplicate `ReferencesJson`, `LoraStackJson`, seed, length, steps, fps or canvas-derived `Width`/`Height` | Duplicated sources of truth violate the single-decision-path rule |
| **V-8** | Autosave, visibly | Debounced (~1.5 s) + save on tab switch + save before navigating away + explicit **Save draft**; always-visible Saved / Unsaved indicator | A silent autosave that lost an edit would be worse than no autosave |
| **V-9** | The library is one shared component, not a second list | One `VideoCompositionList` component; the library page renders it full-page, the asset and session surfaces embed it with a locked filter | Project rule: reuse shared components, never fork a private copy |
| **V-10** | Poster frames | New `PosterRelativePath`, produced by the **already-invoked** ffmpeg verification pass. Missing poster is displayed as an explicit placeholder and (for a completed render) recorded in `VerificationNotes` | Thumbnails must not cost a second video decode per page load |
| **V-11** | Live status without a new mechanism | Poll (~10-15 s, `PeriodicTimer` marshalled via `InvokeAsync`) **only while something is in flight**, plus a manual Refresh. When **B-154** lands, the library adopts that shared mechanism | Existing per-page precedent: `AssetStudioView.razor:540`, `CompositionComposer.razor:1606`. A second refresh mechanism is out of bounds |
| **V-12** | Retention is configured, never hardcoded | The retention value lives on the video function default row (`FunctionModelDefaults`, UI-backed via Model Manager) beside concurrency/lease/poll/retries. Missing value => the pruner fails fast with a diagnostic naming the setting; **no pruning happens until it is configured** | No-fallback rule: no code-only default may silently decide retention |

---

## 4. Data model

### 4.1 `SceneVideos` additions

Both must be added **twice**: to the `CREATE TABLE` text in `SceneVideoRepository.SchemaSql` (`SceneVideoRepository.cs:23`) for new databases, and as a guarded `ALTER TABLE` for existing ones, following the additive-migration idiom already used at `SqlitePersistence.cs:1727` (`SELECT COUNT(*) FROM pragma_table_info('...') WHERE name='...'`).

```sql
ALTER TABLE SceneVideos ADD COLUMN ComposerStateJson TEXT NULL;
ALTER TABLE SceneVideos ADD COLUMN PosterRelativePath TEXT NULL;

CREATE INDEX IF NOT EXISTS IX_SceneVideos_Status_Created
    ON SceneVideos (Status, CreatedUtc DESC);
CREATE INDEX IF NOT EXISTS IX_SceneVideos_Origin
    ON SceneVideos (OriginKind, OriginImageId, OriginCoveragePlanId, CreatedUtc DESC);
```

- `ComposerStateJson` is `NULL` for every existing row. A `NULL` composition is **not** convertible into an editable draft - the UI must say so (V-3 read-only) and offer the explicit "Copy settings into a new draft" action (section 7.3). Never synthesise an empty composer from a null blob.
- `PromptSnapshot` and `SettingsJson` are `NOT NULL`; a draft writes `''` and `'{}'` respectively until it is queued. That is intentional and needs no schema change.
- `SettingsJson` keeps its current meaning: **what was actually submitted** (provenance). `ComposerStateJson` is **what the composer currently holds**. Two blobs, two purposes.

### 4.2 `ComposerStateJson` envelope (version 1)

New file `DreamGenClone.Domain/RolePlay/SceneVideoComposerState.cs` holding a `sealed record SceneVideoComposerState` plus its nested item records, serialized with the repository's existing `JsonOptions`.

```json
{
  "version": 1,
  "style": "one or two sentences, before [Shot 1]",
  "sceneDescription": "the body prose",
  "soundscape": "",
  "soundscapeSilent": false,
  "music": "",
  "musicNone": true,
  "register": "Explicit",
  "canvas": "1344x768",
  "allowUntrainedLength": false,
  "shots": [
    { "number": 1, "cutTime": null, "description": "...", "cameraMotionType": "Static Shot",
      "cameraAmplitude": null, "cameraSpeed": null }
  ],
  "dialogue": [
    { "order": 1, "shotNumber": 1, "speakerName": "Mara", "subjectLabel": "<Subject 1>",
      "identityInfo": "...", "languageCode": "English", "content": "...", "delivery": "quietly",
      "offScreen": false, "voiceover": false, "continuity": "None" }
  ],
  "onScreenText": [ { "shotNumber": 1, "text": "..." } ]
}
```

Rules:

- `version` is mandatory. An **unknown version, unparseable JSON, or a required member missing fails fast** with a diagnostic that names the record id and the version found. Never fall back to an empty composer.
- On load, `canvas` must agree with the record's `Width`/`Height`; a disagreement fails fast rather than silently picking one (V-7).
- Adding a future tab field means bumping `version` and adding a migration branch - that is the point of the envelope, and it must stay cheaper than an `ALTER TABLE`.
- This envelope deliberately does **not** carry the word band; slice D1b removes the band from the page and reads it from the model qualification instead.

### 4.3 Status enum

Append to `SceneVideoStatus` (`SceneVideoRecord.cs:6`) - **never renumber** (the integer is persisted and read back):

```csharp
/// <summary>A saved, resumable composition that has not been queued. Editable; no job exists.</summary>
Draft = 5
```

Terminal/in-flight statuses keep their meanings. A `Draft` row must never carry a `JobId`, must never appear in the lane's claims, and must never be written by the render handler.

### 4.4 Record members to add

On `SceneVideoRecord`: `string? ComposerStateJson`, `string? PosterRelativePath`. No other new members. The composer's typed view of the blob is produced by the domain record, not stored twice.

---

## 5. Domain / repository / service surface

### 5.1 `ISceneVideoRepository` (`DreamGenClone.Application/RolePlay/ISceneVideoRepository.cs`)

```csharp
/// <summary>Updates an editable row in place. Refuses when the row is not a Draft.</summary>
Task<bool> TryUpdateDraftAsync(SceneVideoRecord record, CancellationToken cancellationToken = default);

/// <summary>The newest open (Draft) composition for an origin, for resume-on-open.</summary>
Task<SceneVideoRecord?> GetNewestDraftByOriginAsync(
    SceneVideoOriginKind originKind, string originKey, CancellationToken cancellationToken = default);

/// <summary>Paged, filtered list for the composition library.</summary>
Task<(IReadOnlyList<SceneVideoRecord> Items, int Total)> ListCompositionsAsync(
    SceneVideoCompositionQuery query, CancellationToken cancellationToken = default);

/// <summary>Counts per status for the library's filter chips.</summary>
Task<IReadOnlyDictionary<SceneVideoStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default);
```

- `originKey` is `COALESCE(OriginImageId, OriginCoveragePlanId)`.
- `TryUpdateDraftAsync` must be a single guarded `UPDATE ... WHERE Id = @id AND Status = @draft`, reporting rows-affected so a lost race is a `false`, not a silent overwrite.
- `ListCompositionsAsync` must page server-side (`LIMIT`/`OFFSET`) and return the total, so nothing ever loads the whole table.

`SceneVideoCompositionQuery` (new record, `DreamGenClone.Application/RolePlay/` or the service file):

```csharp
public sealed record SceneVideoCompositionQuery(
    IReadOnlyList<SceneVideoStatus>? Statuses,
    IReadOnlyList<SceneVideoOriginKind>? OriginKinds,
    string? SessionId,
    string? SearchText,
    DateTime? CreatedFromUtc,
    DateTime? CreatedToUtc,
    SceneVideoCompositionOrder Order,
    int Skip,
    int Take);
```

`SceneVideoCompositionOrder`: `Newest`, `Oldest`, `Longest`, `Status`.

### 5.2 `ISceneVideoService` (`DreamGenClone.Web/Application/RolePlay/ISceneVideoService.cs`)

```csharp
/// <summary>Inserts or updates the operator's working composition. No completion guards.</summary>
Task<SceneVideoRecord> SaveDraftAsync(SceneVideoRecord draft, CancellationToken cancellationToken = default);

/// <summary>Deletes a Draft. Refuses (fail-fast) for every other status.</summary>
Task DiscardDraftAsync(string recordId, CancellationToken cancellationToken = default);

/// <summary>The composition an origin route should resume, or null when there is none.</summary>
Task<SceneVideoRecord?> GetDraftForOriginAsync(
    SceneVideoOriginKind originKind, string originKey, CancellationToken cancellationToken = default);

/// <summary>Library query.</summary>
Task<(IReadOnlyList<SceneVideoRecord> Items, int Total)> ListCompositionsAsync(
    SceneVideoCompositionQuery query, CancellationToken cancellationToken = default);

Task<IReadOnlyDictionary<SceneVideoStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default);
```

`EnqueueAsync` changes (V-4):

1. If `record.Id` already names a `Draft` row, **update that row** and promote it: `Status = Pending`, `JobId` minted, `PromptSnapshot` / `CompilerKey` / `CompilerVersion` / `SettingsJson` written, `CreatedUtc` preserved, `UpdatedUtc` bumped, then enqueue the job - **one transaction**.
2. If the row is not a `Draft` (or does not exist), keep today's insert behaviour for the standalone / first-queue case.
3. All existing guards stay exactly as they are (`SceneVideoService.cs:205`, `:212`, `:219`). Do not weaken them for drafts - the guards are what stop an unfinished graph reaching a 25-minute render.
4. The promotion must be idempotent for a duplicate durable-job delivery: only a `Pending` row may be claimed (`TryClaimAsync` already enforces this).

`SaveDraftAsync` semantics:

- Insert when `Id` is empty/new, with `Status = Draft`, `JobId = null`, `CreatedUtc = UpdatedUtc = now`.
- Update in place otherwise, **only if the stored row is still a `Draft`**; otherwise throw with a message naming the status (no silent clone, no silent overwrite).
- Never touch `PromptSnapshot` / `CompilerKey` / `CompilerVersion` / `SettingsJson` - those belong to the submitted render. Recommended: a draft stores **no** prompt at all; the composer compiles on demand and only enqueue stamps it.

### 5.3 Retention (V-12)

No pruning ships until the value is configured. When implemented, the pruner:

- reads the retention value from the video `RolePlaySceneVideo` function default (the same row that already carries concurrency 1, lease 7200 s, poll 5 s, retries [30, 120]);
- **fails fast** naming the setting when the value is absent or invalid;
- prunes only `Draft` rows, never in-flight or complete ones, and reports what it removed through the existing logging pattern.

---

## 6. Composer page: save, resume, reload (`VideoStudio.razor`)

### 6.1 Open sequence

1. `LoadSeedAsync()` as today.
2. For the seeded origins (`SceneImage`, `AssetImage`, `VideoCoveragePlan`) compute `originKey = seed.OriginId` (for a coverage plan use the plan id) and call `GetDraftForOriginAsync`.
3. **Found** -> hydrate every tab from `ComposerStateJson` (plus the record's own columns) and show `Resumed draft - saved 12:41` in the header with a **New composition** button.
4. **Not found** -> build a fresh composer exactly as today, and if the origin has finished compositions, show a one-line banner with a link to the library filtered to that origin (so an operator who expected resume is told where their earlier work went).
5. The **standalone** route never resumes (no origin key). Its saved drafts are reached from the library.

`/composition/{RecordId}` implements V-3: hydrate the same way; every control is disabled with a banner naming the status when it is not a `Draft`, and the Queue tab shows the job as today.

### 6.2 Save sequence

- A single `SaveDraftAsync` call marshalled through one debounce timer (~1.5 s), created on the first edit of any tab.
- Explicit triggers: tab switch, `Save draft` click, and `NavigationManager.LocationChanged` (registered in `OnInitialized`, removed in `Dispose`).
- A `Saved / Unsaved / Saving` indicator sits beside the title, reading the last successful save time. A failed save surfaces the error inline and leaves the indicator Unsaved - never silently swallowed.
- The first successful save mints the row and its id; from then on the URL `/composition/{id}` is stable and can be bookmarked (a "Copy link" action in the header is a cheap win).

### 6.3 Hydration mapping (one source per value, V-7)

| Composer state | Source |
|---|---|
| style / description / soundscape (+silent) / music (+none) / register / canvas / allowUntrainedLength / shots / dialogue / on-screen text | `ComposerStateJson` |
| references (ordered, with roles) | `ReferencesJson` (deserialized into `_references`) |
| LoRA selections | `LoraStackJson`, resolved against the LoRA catalog for display names |
| seed / frame length / steps / ref image size | columns |
| model | `RequestedModelId` -> `ResolveSelectedModelAsync()`; fail fast if the model row no longer exists |
| manual prompt + flag | `PromptManuallyEdited` + the snapshot |
| canvas vs width/height | cross-checked, fail fast on disagreement |

### 6.4 Compiler stamping

A draft carries **no** compiler stamp. `CompilerKey` / `CompilerVersion` are written at enqueue from the compiler that produced the submitted prompt. A draft that was last compiled by an older compiler version shows a "compiled with vX - recompile before queueing" hint (this matters immediately: the compiler is at `1.1.0` after the severity change).

---

## 7. UI: the composition library

### 7.1 Shared component

New `DreamGenClone.Web/Components/Shared/VideoCompositionList.razor` (V-9), reused by:

| Surface | Filter |
|---|---|
| `VideoStudio.razor` (page header "Library" action + a link into the full page) | none |
| Asset surface (`AssetStudioView.razor` + the asset's studio view) | `OriginKind = AssetImage`, `originKey = asset image id` |
| Session surface (`SceneImageStudio.razor`) | `SessionId = @sessionId` |

Do not create a second list, a second card, or a second status-badge vocabulary. If the component cannot express a surface's need, extend it forward.

### 7.2 Library page

New page `DreamGenClone.Web/Components/Pages/VideoCompositionLibrary.razor`:

```
@page "/roleplay/video-studio/compositions"
```

Deep links: `?status=draft|pending|rendering|complete|failed|cancelled&origin=asset|scene|coverage|standalone&sessionId=...&q=...&page=2`.

**Filter chips with counts** (from `CountByStatusAsync`, one `GROUP BY`): `Drafts` - `In progress` (staged / queued / rendering) - `Completed` - `Failed` - `Cancelled` - `All`. The count is visible before the list loads, so "12 drafts" is answerable at a glance.

**Filters:** origin class, session picker / deep link, text search over title and origin id, optional created-date range.

**Sort:** newest (default) / oldest / longest / status. **Paging:** server-side, page size 24, with total and page controls.

**Row content:**

| Column | Source | Notes |
|---|---|---|
| Poster / preview | `PosterRelativePath` for Complete; the origin image for drafts and in-flight rows | explicit placeholder when neither exists |
| Title | column | inline-editable **for drafts only**; untitled rows display a name derived from the origin (asset / scene image label) - never a bare "(untitled)" |
| Status | `Status` | one badge vocabulary, shared with the composer's Queue tab |
| Class / origin | `OriginKind` + `OriginImageId` / `OriginCoveragePlanId` | a session link when `SessionId` is set, so a studio clip is one click from its session |
| Render shape | `Length`, `Fps`, `Width`, `Height`, `Steps`, `Seed`, `ModelIdentifier` | duration = Length / Fps, shown as both |
| Provenance | `CompilerKey`, `CompilerVersion`, `PromptManuallyEdited`, reference count, LoRA count | a visibly older compiler version is flagged, not hidden |
| Verification | `VideoStreamPresent`, `AudioStreamPresent`, `MeasuredLoudnessLufs` vs `LoudnessTargetLufs`, `MeasuredDurationSeconds` | absent (draft / in flight) is shown as "-", not as a failure |
| Timestamps | `CreatedUtc`, `StartedUtc`, `CompletedUtc`, `UpdatedUtc` | drafts show last saved + age |

**Actions** (each gated by status, never offered when impossible):

| Action | Applies to | Behaviour |
|---|---|---|
| Reopen | all | navigate to `/roleplay/video-studio/composition/{Id}` |
| Open mp4 | Complete | the existing `/scene-videos/...` URL (StaticFiles Range support, so it seeks) |
| Queue now | Draft | opens the composer at that record, which is the honest path: compile + validate + promote (V-4) |
| Discard | Draft | `DiscardDraftAsync` after an inline confirm |
| Cancel | Pending / Rendering | existing `CancelAsync` |
| Duplicate as new draft | Draft / Complete | copies the composition state into a new `Draft` (no prompt, no job) |
| Copy prompt / Copy id | all | clipboard |

**Empty states** are per filter and name the entry point, e.g. drafts: "No drafts yet. Open a scene image, an asset image or the Video Composer and the composition is saved as you type."

**Live state (V-11):** poll only while at least one visible row is `Pending`/`Rendering`; stop when none are. Manual Refresh always available. Leave a comment pointing at B-154 as the future owner of this mechanism.

### 7.3 The "copy settings into a new draft" path (pre-draft records)

For records with `ComposerStateJson IS NULL` (everything that exists today, including the live `11980a46` row):

- The composer opens **read-only** with: "This composition was saved before drafts existed, so its shots and dialogue were not stored. Its prompt, references, LoRA stack and settings are intact."
- An explicit **Copy settings into a new draft** action creates a new `Draft` seeded from `PromptSnapshot` + `ReferencesJson` + `LoraStackJson` + `SettingsJson` + the columns, opens it for editing, and states plainly which parts could not be carried over (shots / dialogue / on-screen text are reconstructed as a single untimed shot holding the recorded description). This is a visible, operator-chosen conversion - never an automatic fallback.

### 7.4 Navigation

Add a **Video** group to `NavMenu.razor`:

- `Video Composer` -> `roleplay/video-studio`
- `Video Library` -> `roleplay/video-studio/compositions`

---

## 8. Slices

Each slice must leave the suite green and is independently reviewable. "Exit" is the concrete check.

### D1 - Composition state + status (foundation)

**Scope:** `Draft` status; `ComposerStateJson`; record members; DDL + guarded ALTERs + indexes; domain record and its serialization; the word-band compliance fix (D1b).

**Files:** `DreamGenClone.Domain/RolePlay/SceneVideoRecord.cs`; new `DreamGenClone.Domain/RolePlay/SceneVideoComposerState.cs`; `DreamGenClone.Infrastructure/RolePlay/SceneVideoRepository.cs`; `DreamGenClone.Web/Application/ModelManager/MiniMaxH3ModelSettings.cs` and `DreamGenClone.Web/Components/Pages/VideoStudio.razor` (D1b); `DreamGenClone.DbQuery/Program.cs` plus a read-back query.

**D1b detail:** add `PromptMinWordCount` / `PromptMaxWordCount` to the H3 capability qualification - the `Qualification` bag at `MiniMaxH3ModelSettings.cs:238` already ignores unknown members and `Resolve` fails fast naming the setting, so follow that pattern exactly. Replace `_minWords` / `_maxWords` in the page with the resolved values. A missing value must fail fast with a diagnostic naming the setting - do **not** keep 350 / 500 as a code default.

**Tests:** envelope round-trip (every field, including all three lists and the silent / none flags); unknown `version` fails fast; unparseable blob fails fast; canvas-vs-width/height disagreement fails fast; the DDL is additive on an existing DB (open twice, no error); band resolution fails fast when the qualification omits it.

**Exit:** `Draft = 5` appended; both columns present on an existing dev DB after one open; 350 / 500 appears nowhere in `VideoStudio.razor`.

### D2 - Draft lifecycle in the service

**Scope:** `SaveDraftAsync`, `DiscardDraftAsync`, `GetDraftForOriginAsync`, `TryUpdateDraftAsync`, `GetNewestDraftByOriginAsync`, promote-in-place in `EnqueueAsync`; retention config scaffolding (V-12).

**Files:** `ISceneVideoRepository.cs`, `SceneVideoRepository.cs`, `ISceneVideoService.cs`, `SceneVideoService.cs`, plus the `FunctionModelDefaults` retention field and its Model Manager surface.

**Tests:** insert-then-update keeps one row and one id; update refuses a non-Draft row (naming the status); discard refuses a non-Draft row; promote-in-place yields exactly one row with `JobId` set and `CreatedUtc` preserved; the enqueue guards still reject an empty prompt / zero references / zero length for a promoted draft; the retention pruner fails fast when its value is missing.

**Exit:** queueing a saved draft produces exactly one `SceneVideos` row and one durable job, and the row's id is unchanged from the draft.

### D3 - Composer save / resume / reload

**Scope:** the open sequence (6.1), the save sequence (6.2), hydration (6.3), the compiler-version hint (6.4), New composition, read-only mode for non-drafts (V-3), the `ComposerStateJson IS NULL` conversion path (7.3), E-1.

**Files:** `VideoStudio.razor` (and its `.razor.css` if present - follow `.github/instructions/razor-editing.instructions.md`).

**Tests:** resume hydrates every tab (assert on tab state, not just the record); a fresh origin builds one empty shot; `New composition` leaves the existing draft intact and creates a second row; a non-draft record renders read-only with its status in the banner; a null-blob record offers the conversion action; autosave writes once per debounce window and the indicator tracks it; E-1's notice keys off status.

**Exit:** type into all seven tabs, refresh the browser, and every tab comes back identical - including shots, dialogue and on-screen text.

### D4 - Retrieval surfaces

**Scope:** `/roleplay/video-studio/composition/{RecordId}`; **Reopen** in Recent compositions; embed the shared list on the asset and session surfaces with locked filters; E-2 (a coverage-plan entry point from the beat-pipeline UI).

**Files:** `VideoStudio.razor`, `AssetStudioView.razor`, `SceneImageStudio.razor`, the beat-pipeline surface that shows a `SceneVideoCoveragePlan`.

**Exit:** from the asset page, from a scene image, and from a session, the operator lands back in their draft; the coverage route is reachable from the UI.

### D5 - Library + shared list + posters

**Scope:** `VideoCompositionList.razor`; `VideoCompositionLibrary.razor`; `ListCompositionsAsync` / `CountByStatusAsync` and the query record; the two indexes; poster extraction in the render handler; the Video nav group (E-3).

**Files:** the new shared component; the new page; `SceneVideoRepository.cs`; `ISceneVideoService.cs` / `SceneVideoService.cs`; `SceneVideoRenderingJobHandler.cs` (plus the ffmpeg path used by `SceneVideoAudioProcessor`); `NavMenu.razor`.

**Poster detail:** the handler already invokes ffmpeg for loudness normalization and stream verification; extract one frame (the first, or ~10 % in) to a JPEG beside the mp4 under `SceneVideoRoot` and store `PosterRelativePath`. The existing `/scene-videos` static mapping serves it - no new endpoint. A poster failure is **not** a render failure, but it must be **visible**: append it to `VerificationNotes` and leave the path null.

**Tests:** query filters (each status, origin class, session, search, date range), ordering, paging (skip / take + total), counts per status; the shared component renders a draft with no file and a completed row with a poster; a poster failure records a verification note and still completes the render; the nav group renders both links.

**Exit:** the library lists the live `11980a46` row as Complete with a poster, plus the drafts created during D3 testing; every filter and a multi-page result set are exercised.

### D6 - Housekeeping

**Scope:** retention execution once configured (V-12); draft age display; `Discard` reachable from the composer as well as the library; the docs updates below.

**Docs:** record V-1..V-12 in `specs/Planning/B-152-scene-video-composer/SCOPING.md` (an extension note beside D-3), keep `backlog.md` in step, and note the compiler-stamping rule in `COMPILER-RESEARCH.md` if the composer's prompt path moves.

**Exit:** a configured retention value prunes only drafts; an unconfigured one fails fast and prunes nothing.

---

## 9. Compliance and constraints

- **RP-engine plan gate.** This work touches `DreamGenClone.Web/Application/RolePlay/**`, `DreamGenClone.Infrastructure/RolePlay/**`, `DreamGenClone.Domain/RolePlay/**` and `VideoStudio.razor`. The plan-and-confirm rule applies; scope beyond this document comes back for approval.
- **No fallbacks, anywhere.** No hardcoded runtime defaults, no guessed values, no hidden recovery branches. Missing or invalid required configuration fails fast with a diagnostic that names the setting. `ComposerStateJson` problems fail fast (4.2). A `NULL` composition is a visible read-only state, not an empty editor (7.3).
- **UI-backed controls.** Every new behaviour control (the word band, retention) is configurable in persisted, UI-exposed data - never a code literal. E-4 exists because the current band violates this.
- **Reuse shared components.** One list component (V-9). One status-badge vocabulary. No second picker, card or tray for video.
- **All tests green.** Repository baseline: the 27 `CharacterLoraCoveragePlanGeneratorTests` failures are **pre-existing** (reproduced at HEAD `8e33f0d` in a clean worktree) and unrelated. B-155 may not add a failure, and no test may be skipped to hide one.
- **Razor rules.** Follow `.github/instructions/razor-editing.instructions.md` for `VideoStudio.razor`, the new components and `NavMenu.razor`. Traps already hit in this codebase: single-quoted attributes containing `"`; `@expr.ToString("0.0")` format specifiers (wrap in `@(...ToString(...))`); raw-string parsing of `@"""..."""`.
- **Database rules.** Query and mutate only `dreamgenclone.dev.db`, through `DreamGenClone.DbQuery`. Never commit a `.db` / `.bak`; only `dreamgenclone.snapshot.db` is tracked. Never run `git clean -fd`.
- **No git restore.** Fix forward only.
- **RunPod rule.** Nothing in B-155 changes a pod. Poster extraction uses ffmpeg on the app host, not on the pod.

---

## 10. Verification protocol

**Build and test.** The operator's running app locks `DreamGenClone.Web\bin\Debug`, so build to a temp output path; never kill their process.

```
dotnet build DreamGenClone.Web\DreamGenClone.csproj --nologo -v q -p:BaseOutputPath=D:\src\DreamGenClone\artifacts\tmp\h3build\
dotnet test DreamGenClone.Tests\DreamGenClone.Tests.csproj --nologo -v q -p:BaseOutputPath=D:\src\DreamGenClone\artifacts\tmp\h3build\ --filter 'FullyQualifiedName~SceneVideo'
dotnet test DreamGenClone.Tests\DreamGenClone.Tests.csproj --nologo -v q -p:BaseOutputPath=D:\src\DreamGenClone\artifacts\tmp\h3build\
```

**Database checks** (existing, reusable):

```
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b152-scene-video-job-status.sql <assetImageId>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b152-scene-video-params.sql <assetImageId>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 schema SceneVideos
```

**Live checks for the finished item:**

1. Start the host off the temp build, with the development environment and the working directory at `DreamGenClone.Web` so it reads `data/dreamgenclone.dev.db`:

   ```
   $env:ASPNETCORE_ENVIRONMENT='Development'; Set-Location D:\src\DreamGenClone\DreamGenClone.Web
   dotnet D:\src\DreamGenClone\artifacts\tmp\h3build\Debug\net9.0\DreamGenClone.dll --urls http://localhost:5199
   ```

2. Type into every tab at `/roleplay/video-studio/asset/e4fbbf77bb42428ba21736749658153e`, reload, and confirm every tab is restored (D3 exit).
3. Confirm the library at `/roleplay/video-studio/compositions` lists drafts and the completed `11980a46` row, that filters and paging work, and that Reopen round-trips.
4. Do **not** start a render to test this item - `11980a46` and any earlier clips are the fixtures. A real render is 25-100 minutes on one GPU.

---

## 11. Risks, deferrals and open items

| # | Item | Note |
|---|---|---|
| R-1 | **Existing records are not resumable** | Everything saved before D1 has no `ComposerStateJson`; shots / dialogue / on-screen text cannot be reconstructed from the prose. Mitigated by the explicit conversion path (7.3), never by a fallback. |
| R-2 | Draft rows and the render lane | A `Draft` must never be claimable. Assert it in tests: no `JobId`, excluded from the lane query, never completed by the handler. |
| R-3 | Growth | Drafts are cheap rows but each holds a composer blob; the library must stay server-paged. Retention (V-12) is the long-term answer and is currently unconfigured by design. |
| R-4 | The library's polling vs **B-154** | Polling is a bridge. When B-154 lands, delete it in favour of the shared mechanism rather than keeping both. |
| R-5 | Poster extraction cost | One frame per completed render is negligible, but it must not delay the completion path; run it inside the existing ffmpeg step. |
| R-6 | **Open:** retention semantics | Keep the newest draft per origin with a configured limit, or keep every draft until manually discarded. V-12 fixes *where* the value lives, not *what* it means. Decide before D6 executes pruning. |
| R-7 | **Open:** retry a failed render | The action list deliberately omits Retry - no retry path exists for the video lane today. Adding one is a separate decision (reuse the attempt counter vs a new job). |
| R-8 | Carried over from B-153 (not this item) | The 124-frame acceptance clip **has been rendered** (2026-10-07: ~23 min real GPU render, plus a relaunch that completed end to end), and a completion defect it exposed (`TryCompleteAsync` never bound `$rendering`, so every finished render failed at the last UPDATE) is fixed with lifecycle tests in `SceneVideoRepositoryTests`; the one-click MediaPipe subject crop is not wired (control disabled with its reason); Asset Studio uses per-image card footers because asset-level rows carry no image id; S7 `ref_audios` voice pinning is not started. |

---

## 12. Handoff checklist

1. Read sections 2, 3 and 4 end to end. Re-verify 2.4 (E-1..E-4) still holds at HEAD - they are cheap fixes and easy to have already been patched.
2. Implement **D1** (including D1b) and **D2**. Run the filtered tests, then the full suite.
3. Implement **D3**, and prove the reload test on all seven tabs. That is the operator's actual requirement; if only one thing ships, ship this.
4. Implement **D5** (library + shared list + posters + nav), then **D4** (routes and embeds), then **D6** (housekeeping).
5. Update `SCOPING.md` (decision extension) and `backlog.md` (B-155 state) as part of D6, not afterwards.
6. Report with: the exact build / test commands and results; the DB evidence for a draft row and its promoted row; one DOM assertion or screenshot of the library; and the list of anything still unconfigured (retention).

**Suggested PR boundaries:** (1) D1 + D2, (2) D3, (3) D5 + D4, (4) D6.

---

## Appendix A - evidence commands used for section 2

```
# the live composition behind the asset route
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b152-scene-video-job-status.sql e4fbbf77bb42428ba21736749658153e

# schema of the store and its neighbours
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 schema SceneVideos
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 schema SceneVideoCoveragePlans
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 schema SceneAssetImageEditSessions

# ComfyUI-side proof that a queued graph is this composition (read-only)
(Invoke-RestMethod 'https://comfy.kenacwood.net/queue').queue_running[0][2]   # node types + node inputs
```

## Appendix B - file map

**Changed (existing):** `DreamGenClone.Domain/RolePlay/SceneVideoRecord.cs`; `DreamGenClone.Application/RolePlay/ISceneVideoRepository.cs`; `DreamGenClone.Infrastructure/RolePlay/SceneVideoRepository.cs`; `DreamGenClone.Web/Application/RolePlay/ISceneVideoService.cs`; `DreamGenClone.Web/Application/RolePlay/SceneVideoService.cs`; `DreamGenClone.Web/Application/RolePlay/SceneVideoRenderingJobHandler.cs`; `DreamGenClone.Web/Application/ModelManager/MiniMaxH3ModelSettings.cs`; `DreamGenClone.Web/Components/Pages/VideoStudio.razor`; `DreamGenClone.Web/Components/Pages/AssetStudioView.razor`; `DreamGenClone.Web/Components/Pages/SceneImageStudio.razor`; `DreamGenClone.Web/Components/Layout/NavMenu.razor`; `DreamGenClone.DbQuery/Program.cs`; `specs/Planning/B-152-scene-video-composer/SCOPING.md`; `specs/Planning/backlog.md`.

**New:** `DreamGenClone.Domain/RolePlay/SceneVideoComposerState.cs`; `DreamGenClone.Web/Components/Shared/VideoCompositionList.razor`; `DreamGenClone.Web/Components/Pages/VideoCompositionLibrary.razor`; `DreamGenClone.Tests/RolePlay/SceneVideoDraftTests.cs`; `DreamGenClone.Tests/RolePlay/SceneVideoLibraryQueryTests.cs`.

**Already present and reusable:** `DreamGenClone.DbQuery/queries/b152-scene-video-job-status.sql`; `DreamGenClone.DbQuery/queries/b152-scene-video-params.sql`.

## Appendix C - the two surface classes

| Class | Origins | Entry points today | Resume key | Separate retrieval surface |
|---|---|---|---|---|
| **One-off / Asset Manager** | `AssetImage`, `Standalone` | `AssetStudioView.razor:202`, `ReviewDeck.razor:527` | `OriginKind + OriginImageId` (standalone never resumes) | the asset's own composition list (D4) + the library |
| **Studio** | `SceneImage`, `VideoCoveragePlan` | `SceneImageStudio.razor:1556` and `:1632`, `CompositionComposer.razor:420`, `SceneImageGallery.razor:222`, `ReviewDeck.razor:528`; the coverage route has **no** entry point (E-2) | `OriginKind + OriginImageId`, or the coverage plan id; `SessionId` / `InteractionId` recorded for grouping | the session's composition list (D4) + the library |
