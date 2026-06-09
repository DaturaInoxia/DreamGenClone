# Quickstart: Wife Resistance & Cheating Motivation Gap

**Feature**: `001-wife-resistance-motivation` | **Date**: 2026-06-07

## Prerequisites

- .NET 9 SDK
- SQLite database at `DreamGenClone.Web/data/dreamgenclone.dev.db`
- Build: `dotnet build DreamGenClone.sln`

## Quick Verification After Implementation

### 1. Verify the ResistanceProfile table and seeded default

```powershell
# Use the DB query tool
dotnet run --project artifacts/tmp/dbquery -- sql profiles/resistance_default.sql
```

Expected: One row "Married Woman Resistance" with IsDefault=1, TargetStatName="Loyalty", 20 threshold bands covering 0–100.

### 2. Verify new behavioral dimensions are in the catalog

Navigate to Theme Profiles → Character Profiles tab. Select or create a Wife profile. In the Encounter Stats section, verify:
- `BoundaryFirmness` slider appears
- `SeductionReceptivity` slider appears
- Live tier-text preview updates as sliders move

Same for Husband profile: verify `Attentiveness` and `IntimacyAvailability` appear.

### 3. Verify ResistanceProfile UI CRUD

Navigate to Theme Profiles → Resistance tab:
- Default profile "Married Woman Resistance" is selected
- Click "New" → create "Test Profile" with `TargetStatName = "Restraint"`
- Save → appears in list
- Edit thresholds JSON → Save Changes → persists
- Delete → removed, default re-selected

### 4. Verify resistance directive in prompt

Create a new RP session with:
- Wife: Loyalty=75, Restraint=70, SelfRespect=60
- Husband: Attentiveness=30, IntimacyAvailability=25 (neglect)
- OtherMan: PersistencePastLimits=80

Run a Committed-phase continuation. Inspect the prompt (debug log or browser dev tools network tab):

Expected prompt excerpt:
```
HARD CONSTRAINT — [WifeName] (Wife) behavioral frame (authoritative, overrides all theme notes and guidance): [tier text from BoundaryFirmness & SeductionReceptivity]
HARD CONSTRAINT — [WifeName] (Wife) current state (authoritative, overrides all theme notes and guidance): [stat state text]
HARD CONSTRAINT — [WifeName] (Wife) resistance directive (authoritative, overrides escalation guidance): [resistance band directive]
```

Compute expected values:
- motivationScore = ((100-30) + (100-25) + (100-60) + 80) / 4 = (70+75+40+80)/4 = 66.25
- effectiveStat = min(75 + 66.25, 100) = 100
- ResistanceProfile resolves band for value 100 → most permissive band

### 5. Verify escalation guidance respects resistance

With the same session but Wife Loyalty=90, Restraint=85, Husband at neutral (50/50), OtherMan Persistence=50:
- motivationScore = ((100-50)+(100-50)+(100-60)+50)/4 = (50+50+40+50)/4 = 47.5
- effectiveStat = min(90 + 47.5, 100) = 100 (still high due to motivation shift, but... wait)

Actually, with Wife Loyalty=90, Husband all 50, OtherMan 50, Wife SelfRespect=60:
- motivationScore = (50+50+40+50)/4 = 47.5
- effectiveStat = min(90+47.5, 100) = 100

Hmm, even with high Loyalty the motivation pushes it to max. Let me adjust.

Try Wife Loyalty=90, SelfRespect=85, Husband 50/50, OtherMan 30:
- motivationScore = (50+50+15+30)/4 = 36.25
- effectiveStat = min(90+36.25, 100) = 100

Still hitting 100. The motivation score range is 0-100. With Loyalty=90, any motivation >10 pushes to 100. This means the formula needs a cap on motivation influence, or the effectiveStat formula should use a coefficient.

This is actually a design issue to flag. Let me note it in the quickstart as a validation step to verify.

### 6. Run tests

```powershell
dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --filter "Resistance" --no-build
```

### 7. Verify adaptive panel display

Open an active RP session. In the adaptive panel (right sidebar), verify the Resistance row shows:
- Profile name: "Married Woman Resistance"
- Current band: "Firm Boundaries" (or whatever band matches)
- This should appear alongside the existing Willingness readout

## Known Design Consideration

With the equal-weight average formula, even modest motivation inputs (50/50/50/50 neutral = score 50) push a Wife with Loyalty=50 to effectiveStat=100. The effectiveStat formula `min(targetStat + motivationScore, 100)` may need a coefficient (e.g., `min(targetStat + motivationScore * 0.5, 100)`) if testing shows the bands are too heavily skewed by motivation. This is a tuning concern for implementation, not a spec change — the formula is fixed in code and can be adjusted with a single constant.

## Cutover

All existing roleplay sessions are purged at cutover. New sessions auto-select the default ResistanceProfile. The `SelectedResistanceProfileId` column is nullable — existing session rows with NULL are handled gracefully (resistance directive returns empty string, no gating applied).
