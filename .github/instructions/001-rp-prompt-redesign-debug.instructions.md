---
applyTo: 'DreamGenClone.Infrastructure/RolePlay/**/*.cs,DreamGenClone.Web/Application/RolePlay/**/*.cs,DreamGenClone.Web/Domain/RolePlay/**/*.cs,DreamGenClone.Tests/RolePlay/**/*.cs,DreamGenClone.Web/Components/Pages/RolePlay*.razor,DreamGenClone.Web/Components/Pages/RolePlay*/**/*.razor,specs/001-rp-prompt-redesign/**'
description: 'Debug session rules: analyze→plan→confirm→execute, debug record creation, spec artifact references, build+test protocol.'
---
# 001-rp-prompt-redesign — Debug Session Rules

**Created:** 2026-07-17  
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
- `specs/001-rp-prompt-redesign/spec.md` — Formal specification
- `specs/001-rp-prompt-redesign/tasks.md` — Task breakdown
- `specs/001-rp-prompt-redesign/research.md` — Research decisions
- `specs/001-rp-prompt-redesign/plan.md` — Implementation plan
- `specs/001-rp-prompt-redesign/data-model.md` — Data model
- `specs/001-rp-prompt-redesign/contracts/*.md` — All contracts
- `specs/Planning/rp-prompt-improvement-plan.md` — Detailed design reference

### 4. Validate Every Change With Clean Build + New Session
- After EVERY code change, the user will do a **clean build** and **fresh RP session**.
- Old builds, cached DLLs, or stale DB state are never acceptable reasons for failure.
- If a change passes tests but fails at runtime, the change is wrong — fix it.

### 5. Prompt Design Principles (from spec)
- 18 slots across 3 attention zones (A/B/C)
- 2 prompt variants: Character + Narrative (both first-class)
- 5 actor profiles: Player, NPC Present, NPC Non-Present, Narrative, Custom
- No hardcoded defaults — all config is UI-backed, fail-fast when missing
- No duplication — each piece of content writes to exactly one slot
- Token budget enforced with trim priority (GAP-3)

### 6. DB Interaction Rules
- Use `python` with `sqlite3` for all DB queries
- Use `helpers/dbq.ps1` for canonical tooling when available
- Never modify the production DB directly without user confirmation

### 7. Build + Test Protocol
- Build: `dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore`
- Test: `dotnet test DreamGenClone.Tests --no-build --filter "FullyQualifiedName~RolePlay.Prompts"`
- Full test: `dotnet test DreamGenClone.Tests --no-build`
- Stop web app before building: `Stop-Process -Name dotnet -Force -ErrorAction SilentlyContinue`

### 8. Debug Issue Recording (MANDATORY)
For EVERY issue reported during this debug session:
1. **Create a debug record** at `specs/001-rp-prompt-redesign/debug/###-title.md`
2. Number sequentially (001-, 002-, 003-...)
3. Each record MUST contain:
   - **Report**: What happened. Session ID, interaction ID, error message, symptoms.
   - **Analysis**: Root cause. Code paths traced, DB state, spec artifacts consulted.
   - **Plan**: Proposed fix with file list and change description.
   - **Resolution**: What was actually changed (file diffs summary).
   - **Validated**: Confirmed fixed by user? Date/time. Leave as `[ ] pending` until confirmed.

All debug records live under: `specs/001-rp-prompt-redesign/debug/`
- Stop web app before building: `Stop-Process -Name dotnet -Force -ErrorAction SilentlyContinue`
