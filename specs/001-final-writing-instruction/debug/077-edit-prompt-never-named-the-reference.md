# Edit 2.1: the prompt never names its reference images (and what was sent was unreadable)

## Report

Reported 2026-10-15 from the Scene Image Editor, with two result rows:

> does the edit 2.1 prompt need to have the correct `<imageN>` in the prompt like the create does? is this hidden from
> the user or just not implemented properly? I was expecting for the compiled prompts to include the `<image>`
> references? some edits are not working properly, not sure if i am doing it wrong or system is not properly
> implmented. image `d7e2521b-da86-40ec-84e5-611334791883` shows 2 faces now, not even close to what was askded.
> `70cb3aa7-2153-43bd-9db1-63c359cc55ac` shows more than 2 arms, keeping previous position and added new postition of
> arms.

| Row | Compiled prompt | References bound | Result |
|---|---|---|---|
| `d7e2521b…` | `The woman face is looking to the right.` (39 chars) | **one** `Face` binding — pack face, label `3/4 Right`, ordinal 1 | a SECOND face composited into the frame |
| `70cb3aa7…` | `The grey shirt hangs over the side of the basket on the ground. The woman's hands are at the button of her shorts, and she is starting to undo them.` | **none** | a second pair of arms, the first pair kept |

## Analysis

### The tag threshold counted references, not images (the reported "not implemented properly")

`ReferenceRoleClauses.SendsTags(referenceCount) => referenceCount >= 2` counted the REFERENCES. An edit sends its
SOURCE image first — the 2.1 edit graph wires it to `images.image_1` and reference *i* to `image_{i+2}` — so one
reference beside it is already TWO images, and the vendor rule the same method quotes ("For Multi-Image Input
(N >= 2) … mandatory; for single-image input (N = 1), do NOT use tags") already applied. The codebase had taught the
OFFSET to the numbering (`TagFor`: *"the source image occupies slot 1, so the first reference is `<image2>`"*) and left
the THRESHOLD counting the old thing, so **every one-reference edit emitted no tag at all**.

Measured on the row: one `Face` binding, `AppliedReferenceBindingsJson` carrying it at ordinal 1, and a `PromptSnapshot`
of 39 characters containing no `<imageN>`. The face was uploaded to the model as image 2 and never named, so the role
clause that would have said *"Keep each person's face, hair, skin tone, build and clothing exactly as the reference
image that carries them shows it"* was never emitted either — a face reference with no name is a picture to composite,
which is what came back.

**This is not "hidden from the user": it was never emitted.** For two or more references the tag block does work, and
it is assembled through `FinalImagePromptPlanBuilder.BuildEditInstruction` (`MediaEditImageEditingJobHandler:148`).

### What was sent was unreadable (why the operator could not tell)

The block is appended at SEND time, in memory, and never rewrote `PromptSnapshot` (which is checksum-pinned by the
compiler provenance and validated before a run). So no row could answer "what did the model actually read?" — the one
question the report needed. `SentInstructionSnapshot` did not exist.

### `70cb3aa7` is a different failure, and no tag was involved

It bound NO references, so the source was the only image and "no tags" was correct. Its compiled prompt is a
present-tense STATE DESCRIPTION built from a narrative intent (`the grey shirt is now hanging over he side…`), and it
carries none of the three things the compiler's own contract requires: the target state stated directly, for a
position change "the whole body that moves together", and **what stays unchanged**. A description of new content is an
invitation to add it.

### The same gap is systemic, and it rules out one candidate fix

25 recent completed edits sampled from the store: **most compiled prompts are echoes or light rewrites of the intent
with no preservation clause** — including `enlarge the man's penis slightly` → verbatim, and
`the woman has removed the tank top…` → verbatim. Both compiled `ready` and both are usable instructions. The
compiler's contract calls any echo "a failed compilation", so a REFUSAL rule for echoes (the initially-proposed
`P3`) was measured against that sample: **7 of 9 would have been rejected**, including working ones. v2 of this
compiler prompt already demonstrated the same cost (8 attempts, 0 ready, debug 073).

The structured output is where the missing half already lives. For `d7e2521b` the attempt recorded:

```
requestedChanges: ["keep the woman's clothing unchanged", "keep her pose and expression unchanged",
                   "keep everything else in the setting unchanged"]
preserve:         ["the woman's identity"]
compiledPrompt:   "The woman face is looking to the right."
```

`NormalizeTerminalState` already REQUIRES `preserve` to be non-empty; the prose simply never restates it.

## Plan

**P1 — the tag rule counts the images the model receives.** `SendsTags(referenceCount, leadingImages)`,
plumbed through `BlockFor` / `TagsFor` / `TaggedReferenceCount`; the edit passes
`LeadingImageCount.Edit`. A generate passes none, so every generate path is byte-identical and the operator-approved
2026-10-13 "one reference is never numbered" behaviour for compositions is untouched.

**P2 — record the instruction that was actually sent.** New nullable `SentInstructionSnapshot` on both image stores,
written by the edit handler at completion, because `PromptSnapshot` is the accepted revision and cannot be repurposed.

**P3 — the compiler's contract for the PROSE half: NOT implemented, and the proposed instrument was wrong.** See
*Open* below.

## Resolution

Shipped P1 + P2. Files:

| File | Change |
|---|---|
| `ImageStep/ReferenceRoleClauses.cs` | `SendsTags` / `TaggedReferenceCount` take `leadingImages`; `BlockFor` / `TagsFor` validate the negative count BEFORE deciding tags, then count images; docs updated |
| `ImageStep/FinalImagePromptPlan.cs` | `Validate` / `PromptTagIssues` / `BuildMap` pass `leadingImages` through the same owner |
| `Domain/RolePlay/SceneImageRecord.cs`, `SceneAssetModels.cs` | `SentInstructionSnapshot` |
| `Infrastructure/RolePlay/SceneImageRepository.cs`, `SceneAssetRepository.cs` | column in every INSERT/SELECT/upsert/reader/CREATE + migration; the completion updates write it |
| `Editing/IMediaEditSubjectWriter.cs` | `MediaEditRunOutput.SentInstruction` |
| `Editing/MediaEditImageEditingJobHandler.cs` | passes the assembled instruction |
| `Editing/SceneImageMediaEditSubjectWriter.cs`, `SceneAssetMediaEditSubjectWriter.cs` | persist it |

A column-ordering hazard was hit and fixed during this work: the asset reader is ORDINAL, and appending
`SentInstructionSnapshot` between `GateResultsJson` and the `LocationAnalyzed*` columns (which another agent had
appended at the end) put two properties on index 39 and gave `LocationAnalyzedUtc` another field's value — caught by
`LocationImageAnalysisStorageTests`, fixed by placing the new column truly last.

Tests: 3 clause tests rewritten/added (a one-reference edit is numbered `<image2>`; the statement rides beside the
tag; the tag rule counts images — with the generate case pinned unchanged), the guard test updated for the new
signature and extended to forbid a raw-count call in the plan, plus two end-to-end assertions that the row records
exactly the instruction the editor read.

## Open — P3 (needs an operator decision, not more code)

The compiler's PROSE must carry what stays unchanged. Two instruments, and the evidence favours the second:

1. **Refuse** a `ready` result whose prose echoes the intent and/or states no preservation. Measured cost: it would
   reject most of the operator's current, working edits (7 of 9 sampled), and re-introduces exactly the failure v2 was
   rolled back for (debug 073).
2. **Assemble** the preservation sentence at send time from the compiler's own structured `preserve` (already
   required, already persisted on the attempt), in the same place and through the same one owner that already appends
   the reference clause. No model-compliance dependency, no false refusals.

---

## Follow-up (same report, 2026-10-15) — the compiler against the vendor's edit guidance

The operator asked whether the edit prompt should be as long and detailed as the create prompt, and whether the
compiler follows the Qwen repo. Measured before changing anything:

| Operation | Rows | Avg | Max | Under 200 chars |
|---|---|---|---|---|
| Generate (Qwen-2.1 long-form, N1) | 27 | **737** | 1606 | 2 of 27 |
| Edit (Qwen-2.1, N3) | 22 | **122** | **238** | **20 of 22** |

The 2.1 edit compiler DOES exist and IS grounded in the vendor's material — [research/qwen-2-1-prompt-enhancer.md](../../Planning/B-135-image-playground/research/qwen-2-1-prompt-enhancer.md),
read from `QwenLM/Qwen-Image-2.1` @ `7307809`, path `prompt_rewrite/` — as **Route 1** (adopt the vendor's rules into
our own prose). Route 2 (vendor the prompts byte-exactly) stays parked: it needs the PE checkpoints (fine-tuned
Qwen3.5-VL 9B), and the vendor states the stock model will not reliably emit the contract.

Three vendor edit rules were never carried, and the measurements say they matter:

1. **"Write it as an instruction, leading with the operation."** That phrase is in the research doc and nowhere in our
   compiler; v2 had instead reframed the contract as an *"image-anchored prose state description"* — to fix the echo
   problem, which the numbers above show it did not fix. A description of the resulting picture invites the editor to
   render that picture as new content, which is the shape of both reported failures.
2. The separate decision for text rendered INTO the image (the vendor's 3-step language priority).
3. The canvas rule: an edit keeps the source canvas (the vendor's `ratio_follow = <image1>`), so no size or ratio
   belongs in the prose. The broader canvas decision tree stays an open decision point from the roadmap (D3).

### Resolution (E1–E3)

| File | Change |
|---|---|
| `QwenImage21EditPromptCompiler.cs` | **v5**: the compiled prompt is an INSTRUCTION that leads with the operation; the parts that move are stated as MOVING, never as an addition beside the body already in the frame; preservation is explicitly NOT written into the prose; text rendered into the image gets its own clause with the vendor's language priority; no size/ratio/resolution in the prose |
| `SceneImageEditPromptCompilerBase.cs` | Ready results append the preservation clause when the compiler opts in (`AppendsPreservationClause`) |
| `QwenImage21EditPromptCompiler.cs` | opts in — N3 asked the model for preservation in prose and it demonstrably skipped it |
| `ImageStep/EditInstructionPreservation.cs` | **NEW.** The one owner of the clause, built from the structured `preserve`: bare categories become one statement about what is held, entries that are already statements are used verbatim, each named once, capped at 6, closed by "Change nothing else." |
| `SceneImageEditPromptCompilerResolver.cs` | v4 → historical versions (a stale attempt resolves and fails with the explicit contract-change diagnostic, not a deployment gap) |

**Why the change lands where it does:** the three compile job handlers each take `result.CompiledPrompt` as the
accepted prompt, so the clause reaches the stored revision — and therefore `PromptSnapshot`, the workspace's editable
prompt, and the row's `SentInstructionSnapshot`. The operator can see and edit it, which is the visibility that was
missing.

**The refusal instrument was rejected, deliberately, after measuring it.** Of 9 sampled compiled edits, 7 were echoes
or light rewrites — including `enlarge the man's penis slightly` → verbatim, which worked. Refusing them would break
the operator's normal workflow and re-run v2's failure (debug 073: 8 attempts, 0 ready). The goal — a 39-character
prompt that omits what is held can no longer reach the editor — is met by E3 instead, because preservation is now data.
A *length* floor was also rejected as the wrong instrument: the reported 39-character prompt is not wrong for being
short, it is wrong for describing a picture instead of issuing an operation.

### Validation

- Full suite: **4360 passed / 27 failed / 0 skipped** — the 27 being the pre-existing LoRA-coverage failures.
- New tests: `EditInstructionPreservationTests` (8 — both entry shapes, dedupe, cap, empty, append), the 2.1
  instruction now pins the v5 rules (lead with the operation, preservation-as-data, language, canvas), the 2511
  compiler is pinned as NOT receiving the appended clause, and the shared schema/parse contract test now asserts the
  structured result is identical while the assembled instruction deliberately differs.


## Validated

[ ] pending — awaiting the operator's confirmation that a one-reference edit now names its reference and reads
differently, and a decision on P3.

Full suite: 4338 passed / 27 failed / 0 skipped — the 27 being the pre-existing LoRA-coverage failures already
recorded in the backlog.
