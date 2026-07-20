---
applyTo: 'DreamGenClone.Domain/StoryAnalysis/SteeringProfile.cs,DreamGenClone.Web/Domain/Scenarios/NarrativeSettings.cs,DreamGenClone.Infrastructure/StoryAnalysis/**/*.cs,DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs,DreamGenClone.Web/Application/RolePlay/**/*.cs,DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs,DreamGenClone.Web/Domain/RolePlay/**/*.cs,DreamGenClone.Tests/RolePlay/**/*.cs,DreamGenClone.Tests/StoryAnalysis/**/*.cs,DreamGenClone.Web/Components/Pages/ThemeProfiles.razor,DreamGenClone.Web/Components/Pages/ScenarioEditor.razor,DreamGenClone.Web/Components/Pages/RolePlayWorkspace.razor,specs/001-final-writing-instruction/**'
description: 'Debug session rules: analyze→plan→confirm→execute, debug record creation, spec artifact references, build+test protocol.'
---
# 001-final-writing-instruction — Debug Session Rules

**Created:** 2026-07-19
**Applies to:** All requests during this debug session

## Non-Negotiable Rules

### 1. Never Change Code Without Plan + Confirmation
For every request:
1. **Analyze** — Identify the root cause. Read relevant code, check DB state, review spec/plan/tasks.
2. **Draft a plan** — Write out what will change, in which files, and why. Estimate blast radius.
3. **Get confirmation** — Present the plan. Wait for explicit "yes" or "go ahead." Never proceed without confirmation.
4. **Execute** — Only after approval.

### 2. Never Use Git Restore
- Do not revert files via `git checkout`, `git restore`, or equivalent.
- All fixes are forward-only code changes.

### 3. Always Reference Specification Artifacts
Before any analysis or plan, consult these files as authoritative references:
- `specs/001-final-writing-instruction/spec.md` — Formal specification
- `specs/001-final-writing-instruction/tasks.md` — Task breakdown
- `specs/001-final-writing-instruction/research.md` — Research decisions (R1-R8)
- `specs/001-final-writing-instruction/plan.md` — Implementation plan
- `specs/001-final-writing-instruction/data-model.md` — Data model
- `specs/001-final-writing-instruction/contracts/slot-17-output-contract.md` — Slot 17 output contract
- `specs/001-final-writing-instruction/contracts/terminology-mapping.md` — Label terminology mapping
- `specs/001-rp-prompt-redesign/spec.md` — Parent feature specification (17-slot architecture)

### 4. Validate Every Change With Clean Build + New Session
- After EVERY code change, the user will do a **clean build** and **fresh RP session**.
- Old builds, cached DLLs, or stale DB state are never acceptable reasons for failure.
- If a change passes tests but fails at runtime, the change is wrong — fix it.

### 5. Prompt Design Principles (from spec)
- 17 slots across 3 attention zones (A/B/C) — frozen per 001-rp-prompt-redesign
- 2 prompt variants: Character + Narrative (both first-class)
- All writing direction consolidated into Slot 17 (FinalInstruction) — no duplication
- Slots 8, 12, 15 are stripped of writing direction content
- No hardcoded defaults — all config is UI-backed, fail-fast when missing (Hard Rule)
- No fallbacks for RP engine values — per repo Hard Rule
- Writer-standard prompt labels: Prose Style, Voice, Tone, Heat Level, Pacing, Scene Direction
- Token budget enforced with trim priority; Slot 17 never trimmed

### 6. DB Interaction Rules
- Use `dotnet run --project artifacts/tmp/dbquery -- <command>` for all DB queries
- Use `helpers/dbq.ps1` for canonical tooling when available
- Never modify the production DB directly without user confirmation

### 7. Build + Test Protocol
- Build web: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore`
- Build tests: `dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore`
- Build full solution: `dotnet build DreamGenClone.sln --no-restore`
- Slot contract tests: `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~SlotContractTests"`
- All RolePlay tests: `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~RolePlay"`
- Stop web app before building: `Stop-Process -Name dotnet -Force -ErrorAction SilentlyContinue`

### 8. Debug Issue Recording (MANDATORY)
For EVERY issue reported during this debug session:
1. **Create a debug record** at `specs/001-final-writing-instruction/debug/###-title.md`
2. Number sequentially (001-, 002-, 003-...)
3. Each record MUST contain:
   - **Report**: What happened. Session ID, interaction ID, error message, symptoms.
   - **Analysis**: Root cause. Code paths traced, DB state, spec artifacts consulted.
   - **Plan**: Proposed fix with file list and change description.
   - **Resolution**: What was actually changed (file diffs summary).
   - **Validated**: Confirmed fixed by user? Date/time. Leave as `[ ] pending` until confirmed.

All debug records live under: `specs/001-final-writing-instruction/debug/`
3. Each record MUST contain:
   - **Report**: What happened. Session ID, interaction ID, error message, symptoms.
   - **Analysis**: Root cause. Code paths traced, DB state, spec artifacts consulted.
   - **Plan**: Proposed fix with file list and change description.
   - **Resolution**: What was actually changed (file diffs summary).
   - **Validated**: Confirmed fixed by user? Date/time. Leave as `[ ] pending` until confirmed.

All debug records live under: `specs/001-rp-prompt-redesign/debug/`
- Stop web app before building: `Stop-Process -Name dotnet -Force -ErrorAction SilentlyContinue`
