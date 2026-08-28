# Data Model: B-042 — Unify Character Stats Profiles with Encounter Behavior Profiles

*Phase 1 output*

---

## Overview

This feature introduces one new persistent entity (`CharacterProfile`), one new static code class (`BehavioralDimensionCatalog`), one new DB table (`CharacterProfiles`), and one schema change to `RolePlayV2AdaptiveStates`. Two existing entities (`BaseStatProfile`, `HusbandAwarenessProfile`) are retired and their data migrated.

---

## New Entity: `CharacterProfile`

**Location**: `DreamGenClone.Domain/StoryAnalysis/CharacterProfile.cs`  
**Replaces**: `BaseStatProfile` + `HusbandAwarenessProfile`

### Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `string` | GUID (no dashes), primary key |
| `Name` | `string` | Display name (e.g., "Cuckold Husband") |
| `Description` | `string` | Human-readable archetype description |
| `TargetGender` | `string` | "Male", "Female", "Any" — filters picker at session creation |
| `TargetRole` | `string` | "Husband", "Wife", "OtherMan", "Any" — filters picker and determines which encounter dims are shown |
| `CharacterStats` | `Dictionary<string, int>` | 7 canonical stats: Desire, Restraint, Tension, Connection, Dominance, Loyalty, SelfRespect (0–100 each) |
| `EncounterStats` | `Dictionary<string, int>` | Role-specific behavioral dimension values (0–100 each; which keys are valid is determined by TargetRole via BehavioralDimensionCatalog) |
| `AdditionalNotes` | `string` | Optional text appended after generated tier text in the behavioral frame |
| `FullOverride` | `bool` | If true AND AdditionalNotes is not empty, the behavioral frame is AdditionalNotes only (skips dimension text generation) |
| `IsSeeded` | `bool` | True for archetype defaults — prevents accidental delete in UI (soft guard) |
| `CreatedUtc` | `DateTime` | UTC timestamp |
| `UpdatedUtc` | `DateTime` | UTC timestamp |

### Business Rules

- `CharacterStats` keys MUST be from `AdaptiveStatCatalog.StatNames` — validated before save
- `EncounterStats` keys MUST be from `BehavioralDimensionCatalog.GetDimensions(TargetRole)` — validated before save  
- All stat values clamped to [0, 100]
- `FullOverride=true` with empty `AdditionalNotes` → treated as `FullOverride=false` (no empty HARD CONSTRAINT)
- `TargetRole="Any"` → `EncounterStats` is empty (no encounter dims for generic profiles)
- `TargetRole="Any"` profiles are NOT shown in the session creation encounter profile picker; they may appear as stat-seed-only options if applicable

### State Transitions

None — this is a configuration entity, not a state machine entity.

---

## New Code Class: `BehavioralDimensionCatalog`

**Location**: `DreamGenClone.Domain/StoryAnalysis/BehavioralDimensionCatalog.cs`  
**Type**: `public static class`  
**Not persisted** — code-defined only

### Supporting Type: `BehavioralDimension`

```
sealed record BehavioralDimension(
    string Name,
    string TargetRole,
    string Tier1Text,   // value ≤ 20
    string Tier2Text,   // value ≤ 50
    string Tier3Text,   // value ≤ 75
    string Tier4Text    // value > 75
)
```

### Dimension Definitions (all roles)

#### Husband Role (6 dimensions)

| Name | Tier1 (0–20) | Tier2 (21–50) | Tier3 (51–75) | Tier4 (76–100) |
|---|---|---|---|---|
| **Awareness** | He is completely unaware that anything unusual is happening. | He has vague suspicions but has not connected them; he acts normally. | He suspects or knows something is occurring but chooses not to confront it. | He is fully aware of the encounter and is present with that knowledge. |
| **Acceptance** | Any discovery would result in immediate angry confrontation. | He is uncomfortable but would not act decisively if confronted. | He has reluctantly come to terms with it and would not interfere. | He is fully at ease; the situation causes him no distress at all. |
| **Voyeurism** | He has no desire to observe; he actively avoids any awareness of it. | He is aware it might be happening but keeps deliberate distance. | He has positioned himself where he might be able to observe if it happens. | He is actively and deliberately watching; he will not interrupt for any reason. |
| **Participation** | He will not participate in any form; he would leave or refuse if asked. | He might allow minor indirect involvement if presented carefully. | He participates in a supporting or enabling role when invited. | He is a co-primary participant; he initiates and engages directly. |
| **Encouragement** | He shows no sign of approval; no words, gestures, or facilitation. | He is passively complicit — he doesn't stop it but offers nothing. | He quietly approves and may signal approval through small gestures or words. | He openly encourages, facilitates, and verbally praises what is happening. |
| **RiskTolerance** | He would shut the encounter down at any sign of exposure risk to others. | He is nervous about risk but would not act unless risk became direct. | He accepts moderate risk; he would manage it rather than stop the encounter. | He is comfortable with significant exposure risk and does not let it interfere. |

#### Wife Role (4 dimensions)

| Name | Tier1 (0–20) | Tier2 (21–50) | Tier3 (51–75) | Tier4 (76–100) |
|---|---|---|---|---|
| **DiscoveryCaution** | She makes no effort to conceal this encounter — she may be loud, unconcerned about being heard, and takes no precautions. | She is mildly cautious but is not actively managing discovery risk. | She is careful — she keeps noise down, is aware of time, and would quickly adjust if risk increased. | She is highly vigilant — managing every sensory detail, checking for sounds, and would stop immediately at any sign of detection. |
| **Exhibitionism** | She is deeply private — she would be distressed if seen or heard; she minimizes every sign of the encounter. | She doesn't seek visibility but doesn't go out of her way to hide it either. | She is comfortable being seen and heard by appropriate parties; visibility adds to the experience. | She actively enjoys being seen and heard during the encounter — visibility is part of what she wants. |
| **EmotionalEngagement** | This is purely transactional — she feels no emotional connection to the other man; it is physical only. | She finds him pleasant but maintains clear emotional detachment. | She has developed some emotional warmth toward him; it shows in how she treats him. | She is developing genuine feelings for him; the emotional component is real and present. |
| **PostEncounterGuilt** | She shows no guilt after the encounter — she behaves completely normally with her husband. | She is slightly subdued but recovers quickly and acts normally. | She is noticeably affected — she may be overly affectionate or slightly withdrawn with her husband. | She is overwhelmed — visibly guilty, over-compensating, or emotionally withdrawn after the encounter. |

#### OtherMan Role (4 dimensions)

| Name | Tier1 (0–20) | Tier2 (21–50) | Tier3 (51–75) | Tier4 (76–100) |
|---|---|---|---|---|
| **HusbandAwareness** | He doesn't know the husband exists — he treats the encounter as uncomplicated; the married context is irrelevant to him. | He is vaguely aware she is married but it doesn't enter his actions. | He knows about the husband and is conscious of that fact during the encounter. | He is fully aware of the husband and actively uses that knowledge in his approach and words. |
| **MarriageContextUse** | He never references the marriage, the husband, or the fact that she is someone's wife. | He may make a passing reference if she brings it up but does not pursue it. | He brings up the married context occasionally as a source of intensity or intimacy. | He actively exploits the married context — he references her husband, her vows, and the forbidden nature as core parts of the encounter. |
| **DiscoveryRisk** | He shows no concern about being discovered — he is reckless about noise, timing, and evidence. | He is mildly aware of risk but makes no deliberate effort to manage it. | He is careful — he manages obvious risks and would adjust behavior if a threat appeared. | He is highly careful — he actively manages every risk of discovery throughout the encounter. |
| **PersistencePastLimits** | He respects every stated or implied limit immediately without hesitation. | He may gently probe once but backs off cleanly when met with resistance. | He persists past initial resistance but stops when limits are stated clearly a second time. | He persistently pushes past resistance and stated limits; he treats reluctance as something to overcome rather than a boundary. |

### Tier Resolution Logic

```
value ≤ 20  → Tier1Text
value ≤ 50  → Tier2Text
value ≤ 75  → Tier3Text
value > 75  → Tier4Text
```

Boundary condition: value exactly 20 → Tier1 (inclusive).

---

## Modified Entity: `AdaptiveScenarioState`

**Location**: `DreamGenClone.Domain/RolePlay/AdaptiveScenarioState.cs`

### Change

| Old | New |
|---|---|
| `public string? HusbandAwarenessProfileId { get; set; }` | `public Dictionary<string, string> CharacterEncounterProfileIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);` |

`CharacterEncounterProfileIds` is a dictionary keyed by **character ID** (same key as `CharacterStats`), value = `CharacterProfile.Id`. Empty dictionary = no encounter profiles bound for any character.

**Serialization**: Serialized as a JSON object column `CharacterEncounterProfileIdsJson TEXT NULL` in `RolePlayV2AdaptiveStates`. Follows the same pattern as `CharacterSnapshotsJson`.

### Backward Compatibility

During session load: if `CharacterEncounterProfileIdsJson` is NULL but `HusbandAwarenessProfileId` is set, synthesize an entry: find the character with `Role="Husband"` in the session and add `{ husbandCharId → HusbandAwarenessProfileId }`. Mark the session dirty for re-save.

---

## DB Schema Changes

### New Table: `CharacterProfiles`

```sql
CREATE TABLE IF NOT EXISTS CharacterProfiles (
    Id                  TEXT NOT NULL PRIMARY KEY,
    Name                TEXT NOT NULL,
    Description         TEXT NOT NULL DEFAULT '',
    TargetGender        TEXT NOT NULL DEFAULT 'Any',
    TargetRole          TEXT NOT NULL DEFAULT 'Any',
    CharacterStatsJson  TEXT NOT NULL DEFAULT '{}',
    EncounterStatsJson  TEXT NOT NULL DEFAULT '{}',
    AdditionalNotes     TEXT NOT NULL DEFAULT '',
    FullOverride        INTEGER NOT NULL DEFAULT 0,
    IsSeeded            INTEGER NOT NULL DEFAULT 0,
    CreatedUtc          TEXT NOT NULL,
    UpdatedUtc          TEXT NOT NULL
);
```

### Modified Table: `RolePlayV2AdaptiveStates`

New column added via migration:

```sql
ALTER TABLE RolePlayV2AdaptiveStates 
ADD COLUMN CharacterEncounterProfileIdsJson TEXT NULL;
```

Migration guard: use `PRAGMA table_info(RolePlayV2AdaptiveStates)` to check column existence before altering.

### Migration Logic (run on app startup, in order)

1. Create `CharacterProfiles` table (`CREATE TABLE IF NOT EXISTS`)
2. Delete "Balanced Baseline" profile from `BaseStatProfiles` (FR-014):
   ```sql
   DELETE FROM BaseStatProfiles WHERE Name = 'Balanced Baseline';
   ```
3. Migrate `BaseStatProfiles` → `CharacterProfiles` (INSERT OR IGNORE so re-runs are safe):
   ```sql
   INSERT OR IGNORE INTO CharacterProfiles 
       (Id, Name, Description, TargetGender, TargetRole, 
        CharacterStatsJson, EncounterStatsJson, AdditionalNotes, 
        FullOverride, IsSeeded, CreatedUtc, UpdatedUtc)
   SELECT 
       Id, Name, Description, TargetGender, TargetRole,
       DefaultStatsJson, '{}', '', 0, 1, CreatedUtc, UpdatedUtc
   FROM BaseStatProfiles;
   ```
4. Migrate `HusbandAwarenessProfiles` → `CharacterProfiles` (INSERT OR IGNORE):
   ```sql
   INSERT OR IGNORE INTO CharacterProfiles
       (Id, Name, Description, TargetGender, TargetRole,
        CharacterStatsJson, EncounterStatsJson, AdditionalNotes,
        FullOverride, IsSeeded, CreatedUtc, UpdatedUtc)
   SELECT
       Id, Name, Description, 'Any', 'Husband',
       '{"Desire":50,"Restraint":50,"Tension":50,"Connection":50,"Dominance":50,"Loyalty":50,"SelfRespect":50}',
       json_object(
           'Awareness', AwarenessLevel,
           'Acceptance', AcceptanceLevel,
           'Voyeurism', VoyeurismLevel,
           'Participation', ParticipationLevel,
           'Encouragement', EncouragementLevel,
           'RiskTolerance', RiskTolerance
       ),
       Notes,
       0, 1, CreatedUtc, UpdatedUtc
   FROM HusbandAwarenessProfiles;
   ```
   Note: Migrated husband profiles get neutral (50) canonical stats. Users will see these in the profile editor and can set correct stat values.

5. Add `CharacterEncounterProfileIdsJson` column to `RolePlayV2AdaptiveStates` (guarded):
   ```sql
   ALTER TABLE RolePlayV2AdaptiveStates 
   ADD COLUMN CharacterEncounterProfileIdsJson TEXT NULL;
   ```

6. Seed the 25 unified archetypes via `EnsureDefaultsAsync` in `CharacterProfileService` (INSERT OR IGNORE guards re-seeding).

---

## Retired Entities (kept as Obsolete for migration, then deleted)

| Entity | Status | Data Fate |
|---|---|---|
| `BaseStatProfile` | `[Obsolete]`, no new writes | Data migrated to `CharacterProfiles`; table retained read-only |
| `HusbandAwarenessProfile` | `[Obsolete]`, no new writes | Data migrated to `CharacterProfiles`; table retained read-only |

After one full release cycle without issues, both classes and their tables can be removed in a cleanup commit.

---

## Validation Rules Summary

| Rule | Where enforced |
|---|---|
| CharacterStats keys are canonical stat names | `CharacterProfileService.SaveAsync` |
| EncounterStats keys match BehavioralDimensionCatalog for the given role | `CharacterProfileService.SaveAsync` |
| All stat values in [0, 100] | `CharacterProfileService.SaveAsync` |
| FullOverride=true with empty AdditionalNotes → treated as false | `CharacterBehavioralFrameGenerator` |
| TargetRole="Any" profiles have empty EncounterStats | `CharacterProfileService.SaveAsync` |
| Character profile picker at session creation filters by TargetRole | `RolePlayCreate.razor` picker logic |
| No behavioral frame injected for characters without a bound profile | `CharacterBehavioralFrameGenerator` |
