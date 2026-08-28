# Quickstart: Final Writing Instruction Consolidation

**Feature**: `001-final-writing-instruction`
**Date**: 2026-07-19

---

## Prerequisites

- .NET 9 SDK
- SQLite (`DreamGenClone.Web/data/dreamgenclone.dev.db`)
- Branch `001-final-writing-instruction` checked out
- Solution builds: `dotnet build DreamGenClone.sln`

---

## Implementation Phases (Dependency-Ordered)

### Phase 1: Data Foundation (no UI, no prompt changes)

1. **D1** — `NarrativeSettings.cs`: Add `Tone`, `Register`, `Focus` fields; deprecate `NarrativeTone` (keep for backward compat)
2. **D2** — `StyleProfiles` table: ALTER TABLE to add `ImmersionDirective`, `ActionDirective`, `WordTargetMin`, `WordTargetMax`, `NarrativeWordTargetMin`, `NarrativeWordTargetMax`
3. **D3** — `SteeringProfile.cs`: Add matching C# properties

### Phase 2: Profile Data Cleanup (DB only)

4. **P1** — DB: Create new "Atmospheric" StyleProfile with populated new fields; DELETE Atmospheric from ToneProfiles
5. **P2** — DB: UPDATE Sensual and Emotional ToneProfile descriptions (per research.md R2 cleanup spec)
6. **P3** — DB: UPDATE Sultry StyleProfile to populate new required fields (per research.md R4)
7. **P4** — DB: UPDATE scenario `135a9237` NarrativeSettings to populate `Tone`/`Register`/`Focus` (per research.md R5)

### Phase 3: Prompt Slot Changes (code)

8. **S1** — `WritingStyleSlot.cs`: Remove writing direction emission; emit only contextual/structural data or a single reference line
9. **S2** — `IntensityPacingSlot.cs`: Remove heat level, contract, and pacing emission; retain only available positions
10. **S3** — `ThemeContractSlot.cs`: Remove phase guidance prose emission (already commented out — confirm and clean up)
11. **S4** — `PromptBuildContext.cs`: Add `ResolvedNarrativeToneData` sub-record and field; extend `ResolvedWritingStyleData` with new SteeringProfile fields
12. **S5** — `RolePlayPromptBuilder.cs` (or resolver): Resolve `NarrativeTone` via 3-tier logic (new Tone → legacy NarrativeTone → null); resolve SteeringProfile new fields with fail-fast validation
13. **F1** — `FinalInstructionSlot.cs`: Consolidated output — Scene Direction (if phase active, Character only) + Writing Instruction block (9 components per research.md R6)
14. **F2** — `SlotContractTests.cs`: Update expected strings for S1, S2, S3, F1

### Phase 4: UI (sequenced last — dedicated agent)

15. **U1** — Style Profile management page: Add editable fields for ImmersionDirective, ActionDirective, Character WordTargetMin/Max, Narrative WordTargetMin/Max
16. **U2** — Scenario narrative settings UI: Add editable fields for Tone, Register, Focus; deprecate/hide legacy NarrativeTone
17. **U3** — UI integration tests (if applicable)

### Phase 5: Validation

18. **V1** — Build + run existing SlotContractTests
19. **V2** — Integration testing for Scene Direction ↔ Writing Instruction ordering (3 methods per FR-013):
    - Manual qualitative review (N sample generations per ordering, 4-item checklist: POV, Heat, Scene Direction, Word Target)
    - Automated scoring script (dialogue presence for narrative variant, word count, POV pronouns)
    - Single-author subjective review
20. **V3** — Spec amendments (FR-014, FR-018, FR-021, FR-023 in the 001-rp-prompt-redesign spec)

---

## Build & Test Commands

```powershell
# Build the solution
dotnet build DreamGenClone.sln

# Build web project only
dotnet build DreamGenClone.Web/DreamGenClone.csproj --no-restore

# Build tests project only
dotnet build DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore

# Run slot contract tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~SlotContractTests"

# Run all role-play tests
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "FullyQualifiedName~RolePlay"

# DB query (inspect data)
dotnet run --project artifacts/tmp/dbquery -- sql artifacts/tmp/dbquery/queries/<query>.sql
```

---

## Key Files

| File | Phase | Change |
|------|-------|--------|
| `DreamGenClone.Web/Domain/Scenarios/NarrativeSettings.cs` | 1 | Add Tone, Register, Focus fields |
| `DreamGenClone.Domain/StoryAnalysis/SteeringProfile.cs` | 1 | Add 6 new fields |
| `DreamGenClone.Web/data/dreamgenclone.dev.db` (StyleProfiles) | 1 | ALTER TABLE migration |
| `DreamGenClone.Web/data/dreamgenclone.dev.db` (ToneProfiles) | 2 | DELETE Atmospheric; UPDATE Sensual, Emotional |
| `DreamGenClone.Web/data/dreamgenclone.dev.db` (StyleProfiles) | 2 | INSERT Atmospheric; UPDATE Sultry |
| `DreamGenClone.Web/data/dreamgenclone.dev.db` (Scenarios) | 2 | UPDATE 135a9237 NarrativeSettings |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/WritingStyleSlot.cs` | 3 | Remove writing direction |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/IntensityPacingSlot.cs` | 3 | Remove heat/pacing; keep positions |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/ThemeContractSlot.cs` | 3 | Confirm phase guidance removed |
| `DreamGenClone.Web/Application/RolePlay/Prompts/PromptBuildContext.cs` | 3 | Add ResolvedNarrativeToneData; extend ResolvedWritingStyleData |
| `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs` | 3 | Consolidated 9-component output |
| `DreamGenClone.Tests/RolePlay/Prompts/SlotContractTests.cs` | 3 | Update expected strings |
| Style Profile management Razor page | 4 | Add new field editors |
| Scenario narrative settings Razor page | 4 | Add Tone/Register/Focus editors |

---

## Validation Checklist

- [ ] Solution builds without errors
- [ ] SlotContractTests pass with new expected strings
- [ ] Slot 8 emits no writing direction
- [ ] Slot 15 emits only available positions
- [ ] Slot 12 emits no phase guidance prose
- [ ] Slot 17 emits Scene Direction (if phase active, Character only) + Writing Instruction (9 components)
- [ ] Missing SteeringProfile field → fail-fast error naming profile + field
- [ ] Missing IntensityProfile → fail-fast error
- [ ] Legacy NarrativeTone fallback works when new Tone is empty
- [ ] Atmospheric appears in StyleProfiles, not ToneProfiles
- [ ] Sensual/Emotional descriptions contain only heat-level language
- [ ] Scenario 135a9237 has Tone/Register/Focus populated
- [ ] UI: Style Profile page has editors for all new fields
- [ ] UI: Scenario narrative settings has editors for Tone/Register/Focus
- [ ] Integration testing: chosen ordering passes all 3 validation methods
