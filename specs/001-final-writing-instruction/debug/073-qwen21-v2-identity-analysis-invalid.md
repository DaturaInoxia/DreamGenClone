# Scene Image Editor Identity tab: a finished analysis reports "Identity analysis queued." and nothing else

## Report

Reported 2026-10-13 from the Scene Image Editor
(`/roleplay/image-editor/8bc36efb-b235-485b-8675-b98ad7754e59/460f8394-26ad-4e9c-867d-761b0a55578d/6b15c869-353e-4985-9c47-a86a700772aa`):

> Identity analysis queued. but never comes back is something broken?

The page-level notice "Identity analysis queued." stayed on screen indefinitely and the Identity panel
kept offering **Analyze faces**, as if the analysis were still running.

The analysis was not hung. The click at 16:34:07 EDT produced compilation attempt
`220f3c6f-69ac-427b-a96d-e8df6cd136d0`, which ran for 9 s (`StartedUtc 2026-10-13T20:34:07.4Z`,
`CompletedUtc 2026-10-13T20:34:16.7Z`) and terminated `Invalid`:

- `sourceSummary`: "A woman is bending over in a grassy yard holding a laundry basket."
- `invalidReason`: "The target 'woman on image left' is not a person, but rather an object (a laundry
  basket)…"

The source image plainly shows a woman bending over, so the verdict is wrong, not the image.

## Analysis

Two independent defects, one behavioural and one presentational.

**Defect 1 — the v2 compiler contract closed off the identity-application path.** Debug record
[072](072-qwen21-compiled-prompt-echo-and-json-leak.md) rewrote the
[`QwenImage21EditPromptCompiler`](../../../DreamGenClone.Web/Application/RolePlay/QwenImage21EditPromptCompiler.cs)
system message today (v1 → v2) to stop compiled prompts from echoing the operator's sentence. Two of its
sentences make an identity request unsatisfiable for the configured compiler model
(`qwen2.5-vl-7b-instruct-abliterated-local`, temperature 0.2, top_p 0.8, 512 max tokens, local LM Studio —
the same model for both versions):

1. "A compiled prompt that merely echoes or lightly rewords the user's sentence is a failed compilation…"
   An identity application's instruction is necessarily short — the reference face carries the detail, not
   the sentence — so the contract gave the model no compliant answer other than a terminal non-ready
   status.
2. "What stays unchanged: the identity, the clothing, the objects the subject holds, and the setting."
   Identity was named as unconditionally preserved, which contradicts a request whose whole content is
   "change the identity".

Measured from the dev DB (`SceneImageEditCompilationAttempts`, today from 18:00 UTC):

| `SystemPromptVersion` | Ready | Invalid | Failed |
|---|---|---|---|
| `qwen-edit-2.1-rules-v1` | 8 | 0 | 0 |
| `qwen-edit-2.1-rules-v2` | 3 | 13 | 1 |

Every identity analysis since 15:04 EDT is `Invalid` (8 of 8); all 42 historical identity analyses that
reached `Ready` were compiled before the v2 text existed. The only variable between the two groups is the
prompt version — same provider, model, schema and intent.

**Defect 2 — the Identity panel has no terminal-but-not-ready rendering.**
[`ImageEditWorkspace.razor`](../../../DreamGenClone/Web/Components/Editing/ImageEditWorkspace.razor)
rendered the panel's not-ready branch as an info line plus the **Analyze faces** button, with no outcome
text. The `CompilationError` property that already carried `invalidReason` was wired only into the Edit
tab's workbench, and the page-level `_statusMessage` set by `IdentityPrepareAsync` was never cleared when
the attempt reached a terminal state. A finished `invalid` analysis therefore rendered identically to one
still running — the reported symptom.

## Plan

1. Probe the candidate prompt against the real compiler model before shipping it: same source image, same
   intent, same temperature/top_p/max_tokens/response schema, system message v1 vs v2 vs candidate v3.
2. Give the 2.1 instruction an explicit identity-application path: scope the expansion rule to edits whose
   detail lives in the sentence, stop naming identity as unconditionally preserved, and state that the
   reference face is supplied to the *editor* so a short instruction naming the target, the reference and
   what to hold unchanged is the complete compilation. Bump `SystemPromptVersion` to
   `qwen-edit-2.1-rules-v3` and keep v1 and v2 resolvable as historical versions.
3. Make the Identity panel state its own outcome: surface the terminal reason and clear the stale "queued"
   notice once the attempt it queued terminates.
4. Cover both with tests, keep the 2511 instruction text unchanged, and run the focused set plus the full
   suite.

## Resolution

- `QwenImage21EditPromptCompiler.cs` — the compiled-prompt contract gained one scoped exception ("The one
  exception is a request that delegates its detail to a reference image — an identity application …"), the
  preservation bullet now reads "everything the request did not name … Identity is a preserved category
  only when the request does not ask to change it; when the request applies an approved identity reference,
  the identity IS the change", and a new paragraph states that an identity application is a valid change
  that never needs clarification, that the reference face is supplied to the image editor, and how to
  compile it. `SystemPromptVersion` → `qwen-edit-2.1-rules-v3`. Schema and parse contract unchanged.
- `SceneImageEditPromptCompilerResolver.cs` — the single historical const became
  `Qwen21HistoricalVersions = ["qwen-edit-2.1-rules-v1", "qwen-edit-2.1-rules-v2"]`, so an attempt queued
  under either earlier contract still resolves and then fails on the handler's explicit
  "contract changed after this attempt was queued" check.
- `ImageEditWorkspace.razor` — new `IdentityAnalysisFailure` (reuses `CompilationError` for Invalid/Failed
  and adds the clarification question) is rendered as a warning inside the Identity panel's not-ready
  branch; `_identityAnalysisAttemptId` records the attempt the panel queued and `RefreshAsync` clears the
  transient "queued" notice the moment that attempt is no longer in flight, ready or not.
- Tests — `QwenImage21EditPromptCompilerTests` gains
  `The21Instruction_AdmitsTheIdentityApplicationPath` (identity clause present, the v2 identity-preserved
  wording gone) and asserts the new clause stays out of the 2511 text; `SceneImageEditPromptCompilerResolverTests`
  gains `qwen-edit-2.1-rules-v2` as a resolving historical version.

## Validated

- [x] Prompt probe against the configured model, real source image and the exact Identity intent
      (`artifacts/tmp/probe/probe_qwen21_identity.py`, 3 runs each):
      `v1 3/3 ready`, `v2 3/3 invalid`, `v3 3/3 ready`. The 27 stale attempts in the DB reproduce v2's
      failure mode exactly ("the request is self-contradictory", "there are no other visible people").
      v3's outputs carry a `headView` on every target (v1's omitted it) and a prose `compiledPrompt`.
- [x] The shipped C# literal is byte-identical to the probed v3 text
      (`artifacts/tmp/probe/verify_shipped_matches_probed.py` → `IDENTICAL`), so the code carries exactly
      the string that was measured, not a re-typed approximation.
- [x] Focused set — `QwenImage21EditPromptCompilerTests`, `QwenSceneImageEditPromptCompilerTests`,
      `SceneImageEditPromptCompilerResolverTests`, `SceneImageEditCompilationJobTests`,
      `MediaEditJobHandlerTests`, `MediaEditCompilationServiceTests`, `ImageEditWorkspace*`:
      **98 passed, 0 failed**.
- [x] Full suite — **4245 passed / 27 failed / 4272 total**. All 27 failures are the pre-existing,
      unrelated `CharacterLora*` workstream already reported in record 072
      (`CharacterLoraCoveragePlanGeneratorTests` 25, `CharacterLoraCellTemplateSeedTests` 1,
      `CharacterLoraCoverageSchemaTests` 1); zero failures touch the edit compiler, the resolver, the
      media-edit job handlers or the edit workspace.
- [!] Verification could not use the default output path: a `bin`-based app instance
      (`dotnet.exe … DreamGenClone.Web\bin\Debug\net9.0\DreamGenClone.dll --urls http://localhost:5177`,
      PID 30456, started 16:30:27) holds the lock on `DreamGenClone.Web\bin\Debug\net9.0\DreamGenClone.dll`,
      so `dotnet build` fails with MSB3027/MSB3021. No process was killed; the builds above ran with
      `-p:BaseOutputPath=artifacts\tmp\build\`.
- [ ] Pending operator acceptance: restart the app (`helpers/publish-and-run.ps1`) and click **Analyze
      faces** again on this image; the panel should list the detected person instead of leaving the
      "queued" notice up.

## Observation (not fixed here — pre-existing, not a regression)

The compiler's face `region` for this image is imprecise in both v1 and v3: it reports a ~150 px box at
`x≈239, y≈406` (normalized 0.233, 0.398) while the woman's face sits around x≈500–600. The bounding box
tracks the *person* on the left of the frame rather than the face, and this is unchanged from v1, so it is
out of scope for this record. It matters for identity work only if a downstream mask uses the region
verbatim; worth its own investigation.
