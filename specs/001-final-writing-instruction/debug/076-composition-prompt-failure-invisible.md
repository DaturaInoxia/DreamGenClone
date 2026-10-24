# Studio Composition: a failed prompt draft is discarded before it can be reported, so a 6-minute timeout looks like "queued"

## Report

Reported 2026-10-14 21:30 from the Studio Composition Composer:

> studio composition krea2 generate prompt was queud but never returned a prompt

then, at 21:54:

> i just noticed the default 2.1 generate prompt is slow now as well

The draft was not stuck: it **failed**. `SceneImagePrompts` row `d33377ed-c6a5-4ce6-905d-12dc62422be1`
(production group `dfda210c`, brief `4b1dfc33`) was created 21:14:55 and updated 21:20:55 — exactly 360 s later —
with `Status = Failed` and

> The request was canceled due to the configured HttpClient.Timeout of 360 seconds elapsing.

and the log agrees:

```
21:14:55 [INF] Enqueued canonical scene image prompt generation: ... PromptRecordId=d33377ed-...
21:20:55 [ERR] Completion (reasoning-aware) failed: Model=deepseek-flash
              System.Threading.Tasks.TaskCanceledException: ... HttpClient.Timeout of 360 seconds ...
                at SceneImagePromptGenerationJobHandler.HandleCanonicalAsync(...)
21:20:55 [WRN] Scene image prompt generation failed: PromptRecordId=d33377ed-... Error=... 360 seconds ...
```

## Analysis

Two separate matters: why the draft took six minutes, and why nothing said so.

### A. The call, and what the two slow models have in common

Drafting is not the image model's compiler. **One** configured text function does it for every image model —
`RolePlaySceneImagePreprocessor` → `deepseek-v4-flash` (DeepSeek API, `MaxTokens` 16,000, provider
`TimeoutSeconds` 360, `ThinkingMode` **Default**). Krea 2 and Qwen-Image-2.1 differ only in which prompt *style*
they ask for, which is exactly why the operator saw both slow ("2.1 … as well").

Measured from the app's own logs (job window = the provider call):

| Day | Runs | Avg | Max |
|---|---|---|---|
| 10-02 | 7 | 9.1 s | 18.1 s |
| 10-03 | 4 | **19.4 s** | 34.2 s |
| 10-04 … 10-13 | 31 | 19.5–24.5 s | 45.2 s |
| **10-14** | 9 | **59.8 s** | **360.1 s (timeout)** |

- There is a real step change on **10-03** (9 s → ~20 s) that never reverted, plus a much worse day on 10-14.
- The prompt did **not** grow: the projected user prompt averages ~10,000 chars on 10-14 against ~15,000 in
  September, so the call is not slow because more text is being sent.
- Every prompt-generation call spends **9,600–20,200 characters of `reasoning_content`** before answering
  (10-14: 20,161 / 17,270 / 12,160 / 9,573). `ThinkingMode = Default` means the app sends no `thinking` kwarg at
  all, so the provider's own default — reasoning on — applies to what is a deterministic rewrite. The heaviest text
  function in the app, `RolePlaySceneBeatAnalyzer`, is explicitly `Disabled`.
- 10-14 also carried provider-side failures: a **503** at 21:49:45
  (`"Service is too busy. We advise users to temporarily switch to alternative LLM API service providers."`) and a
  call that never returned at all (the 360 s timeout above).

**Not established:** the cause of the 10-03 step change. It was probed by diffing the pre-processor system prompt
before and after that date, but the two sampled debug events were different prompt *styles* (Pony vs natural
language), so that comparison is invalid and is not used as evidence here. Nothing in records 072–075 changed this
path (they are the edit compiler, the compiler resolver and the reference-binding/enqueue proofs); the working-tree
diff that does touch it is B-157/B-161 work (dropping the duplicated role block, adding the carryability guard),
which makes the prompt smaller, not larger.

### B. Why the panel said nothing (the actual defect)

`SceneImagePrompt` records have a `Failed` status, the job sets it correctly, and the page even had a banner for
it — but the banner read the wrong record:

```csharp
if (_pendingPrompt is not null && _pendingPrompt.Status != SceneImagePromptStatus.Pending)
{
    if (_pendingPrompt.Status == SceneImagePromptStatus.Complete)
        AdoptCompletedPrompt(_pendingPrompt);
    _pendingPrompt = null;                       // ← cleared for Failed as well
}
```

The markup rendered the failure from `_pendingPrompt?.Status == Failed`. The same `LoadAsync` pass that discovered
the failure nulled that field, and `HasInFlightWork()` then went false so polling stopped — so the banner was
**unreachable dead code**. The operator got `Prompt generation queued (Natural Language).` and then permanent
silence: a timeout is indistinguishable from a draft that is still running.

The sibling surface is correct: `SceneImageStudio.razor` keeps the failed record in `_activePrompt` (it only
refreshes while Pending) and renders the banner from it, and its `RefreshAsync` falls through to
`GetLatestPromptAsync`, which returns a failed record. So the Composition Composer was the one surface that
discarded its own evidence.

## Plan

1. Keep a terminal non-Complete draft instead of discarding it, and render it on the tab that produced it.
2. Clear it when that same style is generated again; leave a failure belonging to the other style alone.
3. Report a failure across a page reload, by reading back the newest failed draft for this group and brief.
4. Clear the stale "queued" status line when the draft terminates.
5. Tests, build, focused + full suite, this record.

## Resolution

`Components/Pages/CompositionComposer.razor` only:

- New `SceneImagePromptRecord? _failedPrompt`, documented with the measured case.
- `LoadAsync` — a terminal non-Complete draft is assigned to `_failedPrompt` rather than dropped, `_pendingPrompt`
  is still cleared, and the stale `_statusMessage` is cleared now that the draft is over.
- New `RefreshFailedPromptAsync()` — on first load, reads `GetLatestPromptAsync(sessionId, interactionId)` and keeps
  it only when it `Failed` **and** belongs to this production group and this compiled brief, so a later success
  clears the notice by displacing it.
- New `FailedPromptStyle` helper (mirrors `PendingPromptStyle`) so the notice appears on the tab the failure belongs
  to.
- Markup banner now reads `_failedPrompt.ErrorMessage` guarded by `FailedPromptStyle == _activeStyle`; the
  unreachable `_pendingPrompt`-based condition is gone.
- `GeneratePromptAsync` clears `_failedPrompt` when it is re-drafting the *same* style, so the notice retires as a
  new draft starts.

Razor self-validation: braces 262/262, parens 662/662; no new Tag Helpers (the existing `alert alert-danger` div
pattern); no Blazor-only syntax introduced; every property used (`Status`, `ErrorMessage`, `PromptStyle`,
`ProductionGroupId`, `CompiledMediaBriefId`) verified on `SceneImagePromptRecord`; layout/directives untouched.

## Validated

- [x] `dotnet build DreamGenClone.Web/DreamGenClone.csproj` — the Razor compiles with **0 Razor errors** and no new
      warning; the only build errors are the `bin` file lock held by the operator's running app
      (`PID 29480`, `DreamGenClone.Web\bin\Debug\net9.0\DreamGenClone.dll`), reported rather than worked around. No
      process was killed; verification used `-p:BaseOutputPath=artifacts\tmp\build3\`.
- [x] Focused set — `SceneImageStudioUiContractTests` (including the new
      `CompositionComposer_SurfacesAFailedPromptGenerationInsteadOfDiscardingIt`),
      `FinalImagePromptPlanGuardTests`, `CompositionComposerNativeReferenceContractTests`,
      `CompositionPoseConsolidationContractTests`: **59 passed, 0 failed**.
- [x] Full suite — **4278 passed / 28 failed / 4306 total**. 27 are the pre-existing unrelated `CharacterLora*` set
      (record 072). The 28th, `SceneImageRepositoryTests.InsertImage_StatusTransitions_Count_List_Delete`, is a
      **flaky pre-existing race** (`ObjectDisposedException … 'SQLitePCL.sqlite3'` — a disposed connection during the
      parallel run, nothing to do with a page's markup): it passes 28/28 twice in isolation and passed in both
      earlier full runs today.
- [ ] Pending operator acceptance: re-draft a Composition prompt on a model whose draft fails (or simply wait for
      the next provider timeout) and confirm the panel now shows `Prompt generation failed: <reason>` instead of
      silence, and that it survives a page reload.

## Reported, NOT changed (operator decision, config not code)

- The 360 s wall is `Providers.TimeoutSeconds` for DeepSeek, and the draft's cost is dominated by reasoning that is
  never disabled (`RolePlaySceneImagePreprocessor.ThinkingMode = Default`). The operator chose to skip the
  measured experiment (set `ThinkingMode = Disabled`, re-draft, read `ReasoningLen`/`DurationMs` from the log), so
  the mechanism is documented and untouched. `MaxTokens` was raised 8,000 → 16,000 on 2026-08-23 (visible in
  `_backup_FunctionModelDefaults_SceneImagePreprocessor`).
- The 10-03 step change remains unattributed; the invalid style-mismatched comparison is recorded above so nobody
  repeats it as evidence.
