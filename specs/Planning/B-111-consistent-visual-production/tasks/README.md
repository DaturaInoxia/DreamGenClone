# B-111 Task Decomposition — Index & Dispatch Protocol

**Purpose:** the bridge between `plan.md` (phases + outcome gates) and the coding agent (file-level
work). This index says which phases are **dispatch-ready** and which are **provisional**, and defines
the exact protocol the coordinator uses to dispatch work.

---

## Readiness state

| Phase | Task file | State | Why |
|---|---|---|---|
| **P0** | `tasks/P0-tasks.md` | ✅ **Dispatch-ready** | Depends only on today's code; fully decomposed to file level |
| **P1** | `tasks/P1-tasks.md` | 🟡 **Provisional** | Run engine; final tasks hardened after P0's qualification-matrix audit lands |
| **P2** | *(not yet written)* | ⬜ Outline in plan.md | Hardened at the P1 gate |
| **P3** | *(not yet written)* | ⬜ Outline in plan.md | Hardened at the P2 gate |
| **P4** | *(not yet written)* | ⬜ Outline in plan.md | Hardened at the P3 gate |
| **P5** | *(not yet written)* | ⬜ Outline in plan.md | Hardened at the P4 gate |
| **P6** | *(not yet written)* | ⬜ Outline in plan.md | Hardened at the P5 gate |

> **Why downstream phases are not decomposed today (this is deliberate, not incomplete).**
> Per governance rule **G3**, a phase is hardened to final file-level tasks *immediately before it
> starts*. P1–P3 intentionally change the code that P4+ will touch (new Run aggregate, new identity
> seam, new reference types). Writing P4's final tasks now would encode assumptions that later phases
> invalidate — which is the exact defect this whole program exists to remove. Each phase's outline
> already lives in `plan.md`; it becomes a `tasks/PN-tasks.md` when its prerequisite gate is recorded
> passed.

---

## Roles

| Role | Who | Does | Never does |
|---|---|---|---|
| **Coordinator** | This chat (Claude Opus 4.8) | Decomposes phases, dispatches tasks, verifies output against the gate, runs the golden-set scorer, keeps state, updates task checkboxes | Write feature code directly |
| **Implementer** | Subagent, model **`GPT-5.6 Luna (Copilot)`** | Implements one bounded task, runs the build + targeted tests, returns a diff summary + test result | Decide scope, skip tests, mark a gate passed |
| **Verifier** | Subagent (may be same model) | Runs the full suite / scorer on demand, reports pass/fail with evidence | Change code to make a test pass |
| **Authority** | You | Approve each phase gate; final visual verdict (P8) | — |

---

## Dispatch protocol (per task)

1. **Coordinator** selects the next `not-started` task whose dependencies are all `done`.
2. **Coordinator** dispatches to `GPT-5.6 Luna (Copilot)` with: the task's Objective, Files, Steps,
   Constraints, Acceptance, and the standing constraints below. The prompt is self-contained — the
   implementer is stateless and sees only what it is given.
3. **Implementer** makes the change, runs the task's stated build/test command, returns a diff
   summary + command output.
4. **Coordinator** verifies: reads the diff, checks it against Acceptance, runs `get_errors` and the
   task's tests. If it fails, the coordinator dispatches a fix (never edits code itself).
5. **Coordinator** marks the task `done` only when its Acceptance holds and tests are green.
6. At the end of a phase, the **Coordinator** runs the golden-set scorer, presents the scorecard, and
   **you** give the verdict. Only then is the next phase hardened and started (G3).

## Standing constraints handed to every dispatch

These are pasted into every implementer prompt. They encode the repo's non-negotiable rules.

```
- Forward-only. NEVER use git restore/checkout --/reset --hard. Fix mistakes with new edits.
- No fallbacks. Missing/invalid config fails fast with explicit diagnostics. No hidden defaults,
  no "best effort" substitute values, no duplicated config-source resolution.
- All tests must pass. After your change, run the specified build + tests and confirm green before
  reporting done. Do not disable or skip tests to hide failures.
- Do only the task's stated scope. No refactors, no extra features, no unrelated cleanup, no
  comments/docstrings on code you did not change.
- Read a file before editing it. Use exact literal strings for edits.
- Razor files: follow .github/instructions/razor-editing.instructions.md (full-context reads,
  no hallucinated tag helpers, micro-edits).
- Report: a summary of files changed, the exact build/test command you ran, and its result.
```

## State & auditability

- Task status lives in each `tasks/PN-tasks.md` checkbox list — the durable record across chat turns.
- Every phase gate result is recorded in `tasks/gate-log.md` (created at the first gate).
- The coordinator updates repo memory (`/memories/repo/b111-controlling-visual-program.md`) when a
  phase closes, so a future session resumes from the right point.

## Escalation

The coordinator stops and asks **you** (never guesses) when:
- a task reveals the plan is wrong (like the SFW-clamp sequencing correction);
- an exit gate's automated score passes but looks wrong to the coordinator (you hold P8);
- a task needs a destructive or shared-system action (per repo operational-safety rules);
- an RunPod worker image must be built or changed (needs your go-ahead + registry update).
