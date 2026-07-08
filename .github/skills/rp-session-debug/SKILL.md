---
name: rp-session-debug
description: 'Debug roleplay sessions in DreamGenClone. Use when investigating prompt content, stat drift, phase transitions, gate evaluations, semantic inference, scenario selection, interaction quality, or session state. Requires an RP session ID. Uses canonical PowerShell + dbq.ps1 tooling — no workarounds.'
user-invocable: true
---

# RP Session Debug

## When to Use

- Investigate **prompt content** sent to the LLM
- Debug **stat drift** — stats not moving or moving wrong
- Diagnose **phase transition** failures or unexpected transitions
- Check **gate evaluations** — why a theme/scenario was blocked or selected
- Verify **semantic inference** — events parsed, scored, applied
- Validate **scenario selection** — FitScore, Confidence, rationale
- Inspect **interaction output** quality and narrative fit
- Trace **debug events** timeline for a session
- Verify prompt **HARD CONSTRAINT** presence
- Check **theme scoring** state and theme machine diagnostics

## Before You Start — Ask The User

**Always begin any RP debug session by asking the user:**

1. **What's the session ID?** (GUID — from the UI url, or I can list recent ones from the DB)
2. **What specifically are you investigating?** (pick from the "When to Use" list — stat drift, prompt content, phase transitions, etc.)
3. **Is there a specific interaction or turn you're looking at?** (interaction ID if known)

If they don't know the session ID, run:
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sessions
```
to list recent sessions and ask them to pick.

If they mention an interaction ID or say "the last one" / "the one that...", try to identify the matching interaction from the DB or payload before diving deeper.

---

## Canonical Tooling (Use These Every Time — No Workarounds)

### Database queries

```powershell
# Full session analysis (quick overview of everything):
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq-session.ps1 -SessionId <guid>

# Individual queries:
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 session <guid>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/<query>.sql <guid>

# Schema inspection:
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 schema <TableName>

# List recent sessions:
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sessions
```

### Prompt extraction

```powershell
# List all PromptBuilt events for a session:
python -c "
import sqlite3, json
sid = 'SESSION_ID_HERE'
c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
evt = c.execute(\"SELECT CreatedUtc, Summary, MetadataJson FROM RolePlayDebugEvents WHERE SessionId=? AND EventKind='PromptBuilt' ORDER BY CreatedUtc\", (sid,)).fetchall()
c.close()
for i,e in enumerate(evt):
    meta = json.loads(e[2])
    prompt = meta.get('prompt','')
    print(f'[{i}] {e[0]} | {e[1][:60]} | {len(prompt)} chars')
"
```

Save extracted prompts to `specs/debug/prompts_{shortId}/` following [prompt-extraction.instructions.md](../../instructions/prompt-extraction.instructions.md). Match prompts to interactions by timestamp proximity (`PromptBuilt.CreatedUtc <= interaction.createdAt`).

### Build & test

```powershell
# Build class libraries (faster, avoids locked webapp DLLs):
dotnet build DreamGenClone.Infrastructure --no-restore
dotnet build DreamGenClone.Web --no-restore

# Run specific tests:
dotnet test DreamGenClone.Tests --filter "FullyQualifiedName~<TestName>" --no-restore
```

## Toolbox — What To Use, And When

Don't do everything. Pick the tools that match the user's question.

### 🏁 Starting Point (always useful first)

Run the full dump once to get orientated:
```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq-session.ps1 -SessionId <guid>
```
This runs all standard queries at once: session overview, adaptive state, turns, character snapshots, stat deltas, theme scores, theme tracker, candidate evaluations, gate evaluations, phase transitions, semantic analysis, semantic evidence, debug events, and prompt HARD CONSTRAINT checks.

**Key things to glance at in the output:**
- `CurrentPhase` — is it what you expect?
- `ActiveScenarioId` — correct scenario bound?
- `SemanticStepSucceeded` — true/false?
- `CharacterSnapshotsJson` — stat values and LastStatDeltas
- `PhaseTransition` history — anything unexpected?

---

### 📊 "Stats aren't moving / moving wrong" — Stat Drift

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 adaptive <guid>
```

Check in `CharacterSnapshotsJson`:
- Are `LastStatDeltas` populated? (non-empty = stats were applied)
- What are the current stat values vs what you expect?
- Check `SemanticStatDeltaBreakdownsJson` for the breakdown of what changed and why

Also check:
- **Web app logs** (`DreamGenClone.Web/logs/`) for `SemanticInference RESPONSE` — did the semantic parse succeed?
- `SemanticStepSucceeded` in adaptive state
- Per-character analysis in [semantic-analysis](#-semantic-inference)
- `AdaptiveStateUpdateSkipped` in debug events — **this is expected, ignore it** (see known issues)

---

### 📝 "What was sent to the LLM?" — Prompt Content

Extract and save prompts:
```powershell
python -c "
import sqlite3, json
sid = 'SESSION_ID_HERE'
c = sqlite3.connect('DreamGenClone.Web/data/dreamgenclone.dev.db')
evt = c.execute(\"SELECT CreatedUtc, Summary, MetadataJson FROM RolePlayDebugEvents WHERE SessionId=? AND EventKind='PromptBuilt' ORDER BY CreatedUtc\", (sid,)).fetchall()
c.close()
for i,e in enumerate(evt):
    meta = json.loads(e[2])
    prompt = meta.get('prompt','')
    print(f'[{i}] {e[0]} | {e[1][:60]} | {len(prompt)} chars')
"
```

Match prompt to interaction by timestamp: `PromptBuilt.CreatedUtc <= interaction.createdAt`. Then save the full prompt file via the prompt-extraction skill.

**In the prompt text, check:**
- **HARD CONSTRAINT lines** — do stat values, theme, phase constraints look correct?
- **Active instruction (persistent)** — any user steer or engine instruction re-injected?
- **Message/Instruction/Narrative Direction** at the end — the per-turn instruction
- **Theme guidance markers** like `[Pacing:fast]`, `[Deepening:subsequent-actors]` — present in the right phase?

---

### 🔄 "Phase won't advance / advanced too early" — Phase Transitions

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/phase-transitions.sql <guid>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/gates.sql <guid>
```

Check:
- `FromPhase` → `ToPhase` in transition history
- `TriggerType` and `ReasonCode` — what triggered the transition
- Gate evaluations — are thresholds being met?
- `PhaseOverrideFloor` in adaptive state — is there a manual override?
- `InteractionsSinceCommitment` / `InteractionsInApproaching` — are you past the required count?
- Phase defaults reference (see known issues) for default pacing/beats per phase

---

### 🚪 "Theme/scenario was blocked or shouldn't have been selected" — Gate & Scenario Evaluation

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/evals.sql <guid>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/gates.sql <guid>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/theme-scores.sql <guid>
```

**Candidate evaluations:**
- `FitScore` vs `UnpenalizedFitScore` — cooldown penalty active?
- `StageAWillingnessTier` — willingness gate passed?
- `StageBEligible` — scenario-level eligibility?
- `Rationale` — why this score
- `DetailsJson` — `FitScoreMultiplier` and `SuccessorCausalityBoost`

**Gates:**
- Threshold values and comparators — are the right rules firing?
- Pass/fail status for each gate

**Theme scores:**
- `Blocked` flags — why blocked?
- `IsScenarioCandidate` — eligible for selection?
- `SuppressedHitCount` — theme being suppressed?

---

### 🔬 "Semantic inference isn't working" — Semantic Analysis

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/semantic-analysis.sql <guid>
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/semantic-applied.sql <guid>
```

**Analysis results:** per-interaction per-character semantic parse — successful or failed?
**Applied evidence:** what signals were detected, what theme/stat deltas were applied.

Also check web app logs for `SemanticInference RESPONSE` or `PARSE-FAILED`.

See semantic mappings soft-skip rules in known issues — theme mappings skip after primary theme is committed, stat mappings always run.

---

### 📋 "What happened during this session?" — Debug Event Timeline

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/debug-events.sql <guid>
```

Look for events by kind to trace the session's execution:
- `PromptBuilt` — timestamps to match interactions
- `SemanticInference RESPONSE` — parse success/failure
- `AdaptiveStateUpdateSkipped` — **EXPECTED, IGNORE** (see known issues)
- `PhaseTransition` — phase changes
- `GateEvaluation` — gate pass/fail
- `CandidateEvaluation` — scenario scoring
- `SessionStateCorrupt` / `MissingRequiredConfig` — fail-fast diagnostics

---

### 💬 "Show me the actual conversation" — Interactions

The session payload JSON has the full interaction list:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/session_payload_start.sql <guid>
```

The `interactions` array structure:
```json
{
  "id": "guid",
  "role": "user" | "assistant",
  "content": "text...",
  "createdAt": "ISO8601",
  "flags": ["flag1", "flag2"],
  "commandType": "continue" | "submit" | null,
  "actorName": "Name",
  "characterId": "guid"
}
```

Check: interaction count, role alternation, content quality, command types per turn.

---

### 🎯 "Specific interaction/turn isn't right" — Interaction-Level Deep Dive

If the user has an interaction ID or can describe which turn:
1. Find the interaction in the payload via its `id`
2. Find the `PromptBuilt` event with `CreatedUtc` just before the interaction's `createdAt`
3. Extract and analyze that specific prompt
4. Check `SemanticInference RESPONSE` for that interaction's analysis
5. Check `CharacterSnapshotsJson.LastStatDeltas` for stat changes after this interaction

### 🎵 "Theme scoring looks wrong" — Theme State

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql artifacts/tmp/dbquery/queries/theme-scores.sql <guid>
```

Also check theme tracker meta for primary/secondary theme selection:
- `ThemeSelectionRule` — how was the primary theme selected?
- `ObservedTurnCount` vs `SelectionMinimumTurns` — has enough time passed?
- `RecentEvidenceJson` — what evidence drove the selection?

## Data Sources Summary

| What | Where | How to Read |
|------|-------|-------------|
| Session metadata | `Sessions` table | `dbq session <id>` |
| Adaptive state | `RolePlayV2AdaptiveStates` | `dbq adaptive <id>` |
| Character stats | `CharacterSnapshotsJson` | `dbq adaptive <id>` |
| Stat deltas | `SemanticStatDeltaBreakdownsJson` | `dbq adaptive <id>` |
| Theme scores | `RolePlayV2ThemeScores` | `dbq themes <id>` |
| Theme tracker | `RolePlayV2ThemeTrackerMeta` | `dbq-session.ps1` |
| Candidate evals | `RolePlayV2CandidateEvaluations` | `dbq evals <id>` |
| Phase transitions | `RolePlayV2PhaseTransitions` | `dbq transitions <id>` |
| Turns | `RolePlayV2Turns` | `dbq turns <id>` |
| Debug events | `RolePlayDebugEvents` | `dbq debug <id>` |
| Prompts | `MetadataJson.prompt` in `PromptBuilt` events | Python extraction |
| Interactions | `Sessions.PayloadJson.interactions` | Python or SQL |
| Gate profiles | `NarrativeGateProfiles` | `dbq gate-profiles` |
| Gate rules | `RPThemeNarrativeGateRules` | `dbq gate-rules <themeId>` |
| Theme profiles | `RPThemeProfiles` + assignments | `dbq theme-profiles` |
| Semantic analysis | `RolePlaySemanticInteractionAnalysisState` | `dbq-session.ps1` |
| Semantic events | `RolePlayV2SemanticEvents` | `dbq-session.ps1` |
| Semantic mappings | `RPThemeSemanticEventMappings` | SQL query |
| Scenario data | `Scenarios` + `ScenarioDefinitions` | `dbq scenario <id>` |
| Completion history | `RolePlayV2CompletionMetadata` | `dbq completions <id>` |
| Formula versions | `RolePlayV2FormulaVersionRefs` | `dbq formula <id>` |
| Web app logs | `DreamGenClone.Web/logs/` | Read log files |
| Theme machine diag | `RolePlayV2ThemeMachineDiagnostics` | SQL query |

## Known Issues and Patterns (Check These First)

### AdaptiveStateUpdateSkipped — IGNORE IT
`RolePlayFeatureFlags:EnableAdaptiveStateUpdates` is **intentionally false** in appsettings.json by design. The old inline keyword-matching path was replaced by the Semantic Engine. `AdaptiveStateUpdateSkipped` in debug logs is **expected, correct behavior** — never cite it as a bug. See `/memories/repo/adaptive-state-updates-flag.md`.

### Character Stats Overwritten (Fixed 2026-06-09)
If stats appear reset, it's likely the old bug where `HydrateV2State()` didn't copy `CharacterSnapshots` from persisted state. Fix is in place. Verify by checking `LastStatDeltas` and `UpdatedUtc` in `CharacterSnapshotsJson`.

### Semantic Mappings Soft-Skip Rules
- Theme-scoring semantic mappings skip once `PrimaryThemeId` is set
- Stat-scoring semantic mappings ALWAYS run while session is live
- No configured mappings for an event = valid state, not an error
- Only fail-fast on: null `_rpThemeService`, confidence out of range, malformed tokens
- See `/memories/repo/roleplay-semantic-mappings-soft-skip.md`

### Phase Defaults (No Marker Present)
| Phase | Pacing | BeatScope | TimeShift |
|-------|--------|-----------|-----------|
| Opening | Medium | Short | Small |
| BuildUp | Medium | Short | Small |
| Committed | Medium | Short | Small |
| Approaching | Medium | Short | Small |
| Climax | Fast | Short | Medium |
| Reset | Slow | Single | None |

### Turn vs Interaction Count
Theme selection observation gate is **turn-based**, not interaction-based. `ObservedTurnCount` increments once per `StartTurnAsync`. Session audits should report both counts. See `/memories/repo/roleplay-turn-vs-interactions.md`.

### Location Truth State
Narrative/system location matches should NOT mass-update all character locations — only set per-character location for explicit actor matches. See `/memories/repo/roleplay-location-truthstate-note.md`.

### Perspective Modes
Defaults on `Scenario.DefaultPersonaPerspectiveMode` / `Character.PerspectiveMode`. Session overrides on `RolePlaySession.PersonaPerspectiveMode`. Prompt builder in `DreamGenClone.Web/Application/RolePlay/RolePlayPerspectivePromptBuilder.cs`. Legacy sessions may need enum backfill. See `/memories/repo/roleplay-perspective-modes.md`.

### Gate Threshold Rules
- **No fallbacks** — gates use configured values only. Fail fast if missing.
- One source resolution path per gate evaluation.
- See [roleplay-gates-no-fallback.instructions.md](../../instructions/roleplay-gates-no-fallback.instructions.md).

## Codebase Map

Key source files for debugging — see [references/codebase-map.md](./references/codebase-map.md) for the full map.

Key directories:
- `DreamGenClone.Web/Application/RolePlay/` — engine services, injectors, prompt composition
- `DreamGenClone.Infrastructure/RolePlay/` — repositories, gate services, scenario selection
- `DreamGenClone.Web/Domain/RolePlay/` — domain models (session, interaction, turn)

## Pre-Baked Query Library

See [references/query-library.md](./references/query-library.md) for the complete list of queries organized by debugging purpose.

## Change Control Rule (MANDATORY)

**Never automatically implement code or data changes during debugging.** When an issue is found:
1. Present the finding clearly — what is wrong, where, and why
2. Wait for explicit user confirmation before touching any file
3. User may: approve the fix, defer to backlog, or break out to a separate planning session
4. Only proceed after explicit "go ahead"

## Output Guidance

After debugging, produce a structured summary:
1. **Session overview**: ID, phase, scenario, interaction count, turn count
2. **Issue identified**: What's wrong, with evidence
3. **Root cause**: Code location, config value, data state
4. **Impact**: What behavior is affected
5. **Recommended fix**: Specific change needed (but don't implement without approval)
6. **Evidence**: Query results, log excerpts, prompt snippets

## Violations Detection Checklist

When auditing whether this skill was followed correctly:
- **Did the agent ask for the session ID and what specifically to investigate** before running anything? (Yes/No)
- **Was the right tool chosen for the problem** rather than running everything? (Stat drift → adaptive state. Prompt → extraction. Phase → transitions + gates. etc.)
- **Were canonical helpers used** (`helpers/dbq.ps1`, `helpers/dbq-session.ps1`) instead of raw sqlite3 or ad-hoc queries? (Yes/No)
- **Was the Change Control Rule obeyed** — findings presented without auto-implementing fixes? (Yes/No)
- **Was `AdaptiveStateUpdateSkipped` treated as expected behavior** rather than flagged as a bug? (Yes/No)
- **Were prompts extracted via the canonical Python command** when prompt analysis was needed? (Yes/No)
