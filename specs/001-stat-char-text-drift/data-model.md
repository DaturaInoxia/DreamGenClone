# Data Model: Stat-Driven Character Instruction Text & Encounter Dimension Drift

**Branch**: `001-stat-char-text-drift` | **Date**: 2026-05-30

---

## Modified Entities

### `AdaptiveStatCatalog` (modified)
**File**: `DreamGenClone.Application/StoryAnalysis/AdaptiveStatCatalog.cs`

Remove the two entries from `CanonicalStats`:
- `Tension` entry — deleted
- `Connection` entry — deleted

`CanonicalStatNames` derives from `CanonicalStats` automatically. All downstream data-driven logic (UI stat panels, `NormalizeComplete`, `CreateDefaultStatMap`, `CharacterProfileService` validation) cascades from this single change.

**After change — 5 canonical stats in order**:
1. Desire
2. Restraint
3. Dominance
4. Loyalty
5. SelfRespect

---

### `CharacterStatProfileV2` (modified)
**File**: `DreamGenClone.Domain/RolePlay/CharacterStatProfileV2.cs`

| Change | Detail |
|--------|--------|
| Remove `Tension` property | `public int Tension { get; set; }` — deleted |
| Remove `Connection` property | `public int Connection { get; set; }` — deleted |
| Add `RuntimeEncounterStats` property | `public Dictionary<string, int>? RuntimeEncounterStats { get; set; }` — nullable; null until first stat delta or profile rebind initialises it |

**JSON compatibility**: Existing `CharacterSnapshotsJson` values containing `"Tension"` and `"Connection"` fields deserialise without error (System.Text.Json ignores unknown properties by default). `RuntimeEncounterStats` is null on deserialising old records; lazy initialised on first stat delta.

**Persistence**: No schema change. `RuntimeEncounterStats` serialises inside the existing `CharacterSnapshotsJson` TEXT column in `RolePlayV2AdaptiveStates`.

---

### `ScenarioGuidanceInput` (modified)
**File**: `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs`

| Change | Detail |
|--------|--------|
| Remove `AverageTension` | `double AverageTension` positional param — deleted |
| Remove `AverageConnection` | `double AverageConnection` positional param — deleted |
| Add `CharacterRuntimeStats` | `IReadOnlyDictionary<string, CharacterStatProfileV2>? CharacterRuntimeStats` — nullable; keyed by character display label (e.g. `"Sarah (Wife)"`) matching the keys used in `CharacterBehavioralFrames` |

---

### `ScenarioGuidanceContext` (modified)
**File**: `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs`

| Change | Detail |
|--------|--------|
| Add `CharacterStatStateTexts` | `IReadOnlyDictionary<string, string> CharacterStatStateTexts` — character display label → synthesized stat state sentence; empty dict when no character has out-of-neutral stats |

---

## New Entities

### `CharacterStatBand` (new record)
**File**: `DreamGenClone.Domain/StoryAnalysis/CharacterStatTextCatalog.cs`

```
record CharacterStatBand(
    string StatName,      // "Desire" | "Restraint" | "Dominance" | "Loyalty" | "SelfRespect"
    string TargetRole,    // "Wife" | "Husband" | "OtherMan"
    string Band1Text,     // value ≤ 20
    string Band2Text,     // value ≤ 50
    string Band3Text,     // value ≤ 75
    string Band4Text)     // value > 75
```

---

### `CharacterStatTextCatalog` (new static class)
**File**: `DreamGenClone.Domain/StoryAnalysis/CharacterStatTextCatalog.cs`

15 entries (5 stats × 3 roles). Mirrors structure of `BehavioralDimensionCatalog`.

**Band thresholds**: value ≤20 → Band1, ≤50 → Band2, ≤75 → Band3, >75 → Band4

**Neutral band**: 35–65 → `IsNeutralBand()` returns true → no stat state text injected

**Interface**:
```
static string? ResolveText(string statName, string targetRole, int value)
    // Returns band text for known combinations; null for unknown stat/role
static bool IsNeutralBand(int value)
    // Returns true when 35 ≤ value ≤ 65
```

**Full text definitions** (15 entries):

#### Desire

| Band | Wife | Husband | OtherMan |
|------|------|---------|----------|
| Band1 (≤20) | she is largely indifferent to physical intimacy; arousal requires sustained effort and explicit encouragement | he has little interest in physical intensity; he is unlikely to initiate or seek escalation | he shows minimal urgency or drive; his approach is casual and low-energy |
| Band2 (≤50) | she has mild interest and responds to gentle encouragement but is not seeking intensity on her own | he has moderate interest and will engage when invited but does not drive escalation | he is present and interested but not pressing; he responds to invitation without pushing |
| Band3 (≤75) | she is noticeably engaged and responds eagerly; she welcomes escalation and shows clear arousal signals | he is actively interested and engaged; he participates readily and may gently push for more | he is focused and persistent; he pursues with clear intent and does not let momentum drop |
| Band4 (>75) | she craves physical intensity with urgency; she will initiate, escalate, and pursue without hesitation | he is intensely driven; he initiates strongly, presses for escalation, and sustains high energy throughout | he is single-minded in pursuit; he applies steady, forceful pressure and does not accept easy deflection |

#### Restraint

| Band | Wife | Husband | OtherMan |
|------|------|---------|----------|
| Band1 (≤20) | she has almost no capacity to hold back; inhibition is functionally absent; she acts on impulse without internal resistance | he exercises almost no self-restraint; he reacts immediately to impulses and does not moderate his responses | he has no impulse control in this context; he says and does what comes to mind without filtering |
| Band2 (≤50) | she can delay or moderate her responses with effort, but her resistance is fragile and gives way under sustained pressure | he applies moderate self-restraint but it bends under pressure; he can be pushed past his usual boundaries | he maintains loose self-control but it yields under sustained or clever pressure |
| Band3 (≤75) | she holds herself in check firmly; she requires significant pressure or trust before lowering her guard | he exercises clear, sustained restraint; he does not let himself be pushed easily | he maintains deliberate self-control; he does not allow himself to be rushed or manipulated into acting |
| Band4 (>75) | she is rigidly self-contained; her inhibition is strong and resistant to erosion under any normal pressure | he is tightly controlled and does not break discipline; he exits or deflects rather than lowering his guard | he is disciplined and careful; he will not take risks or act impulsively regardless of provocation |

#### Dominance

| Band | Wife | Husband | OtherMan |
|------|------|---------|----------|
| Band1 (≤20) | she feels powerless and reactive; she does not direct, steer, or assert — she defers to whatever is placed before her | he is passive and deferential; he follows any lead, does not assert his own preferences, and makes no effort to control outcomes | he is tentative and accommodating; he adjusts to her signals and does not assert pressure |
| Band2 (≤50) | she has a modest sense of agency but yields the lead readily; she participates without asserting direction | he participates willingly but is not asserting direction; he can be led without resistance | he takes a collaborative stance; he matches her energy and does not try to dominate the pace |
| Band3 (≤75) | she has clear personal agency; she expresses preferences, sets the tone, and redirects when she chooses | he is assertive about his role; he shapes the dynamic and does not simply follow | he directs the dynamic confidently; he sets pace and framing and expects compliance |
| Band4 (>75) | she is fully in command of this encounter; she decides its direction, pace, and terms | he is decisive and assertive; he directs the encounter, controls its pace, and does not yield unless he chooses to | he is dominant and controlling; he frames the encounter on his terms and redirects any resistance |

#### Loyalty

| Band | Wife | Husband | OtherMan |
|------|------|---------|----------|
| Band1 (≤20) | her commitment to her marriage is effectively absent; she feels no guilt and faces no internal resistance to transgression | his emotional investment in the relationship is minimal; he is indifferent to its preservation | his awareness of her committed relationship does not constrain him; he treats her as fully available |
| Band2 (≤50) | her loyalty is present but not strong; guilt and hesitation surface occasionally but do not reliably stop her | his commitment is present but soft; it does not exert strong pressure against the current dynamic | he is aware she is married and occasionally acknowledges it, but it does not significantly change his approach |
| Band3 (≤75) | she retains meaningful loyalty; she requires real emotional pressure or deliberate justification before crossing boundaries | he maintains a real sense of commitment; he does not easily dismiss the significance of the relationship | he respects the complexity of her situation and does not push her to ignore or violate her commitments |
| Band4 (>75) | she is deeply committed; her loyalty creates strong internal resistance and genuine guilt at any transgressive thought | he is fully committed; the relationship is his anchor and he actively protects it | he is respectful of her relationship and would not press for anything that violates her commitments |

#### SelfRespect

| Band | Wife | Husband | OtherMan |
|------|------|---------|----------|
| Band1 (≤20) | her self-valuing has eroded; she accepts degrading or compromising acts without resistance and places little value on her own dignity | his self-regard is diminished; he accepts humiliation and does not defend his own worth or standing | he treats her as someone with no meaningful personal limits; he does not feel bound to preserve her dignity |
| Band2 (≤50) | her self-worth is uncertain; she may accept acts that compromise her but shows some unease or reluctance | his self-esteem is inconsistent; he sometimes accepts slights or indignity without pushback | he shows some awareness of her worth but does not strongly prioritise protecting it |
| Band3 (≤75) | she has clear self-worth and expects to be treated accordingly; she will push back on acts that demean or diminish her | he has solid self-respect; he does not accept humiliation and pushes back against diminishment | he treats her with respect and does not push her toward acts she would find degrading |
| Band4 (>75) | she has strong, unwavering self-regard; she maintains firm personal standards and will refuse anything that compromises her dignity | he has unshakeable self-respect; he defines clear boundaries around his worth and enforces them without hesitation | he holds her in high regard and would not pressure her into anything that contradicts her sense of self-worth |

---

### `DimensionDriftRule` (new record)
**File**: `DreamGenClone.Domain/StoryAnalysis/StatToDimensionMappings.cs`

```
record DimensionDriftRule(
    string StatName,       // e.g., "Desire"
    string TargetRole,     // "Wife" | "Husband"
    string DimensionName,  // e.g., "Exhibitionism"
    double Slope,          // positive = stat increase raises dimension; negative = raises dimension when stat decreases
    int Floor,             // minimum clamped value
    int Ceiling)           // maximum clamped value
```

---

### `StatToDimensionMappings` (new static class)
**File**: `DreamGenClone.Domain/StoryAnalysis/StatToDimensionMappings.cs`

**Interface**:
```
static IReadOnlyList<DimensionDriftRule> GetRules(string targetRole)
static void ApplyDelta(Dictionary<string, int> encounterStats, string targetRole, string statName, int statDelta)
    // For each matching rule: encounterStats[dim] = Clamp(current + (int)Round(slope × statDelta), floor, ceiling)
    // No-op if statDelta == 0
```

**Wife drift rules** (8 rules):

| Stat | Direction | Dimension | Slope | Floor | Ceiling | Narrative intent |
|------|-----------|-----------|-------|-------|---------|-----------------|
| Desire | ↑ | Exhibitionism | +0.30 | 0 | 100 | Higher desire → more comfort being seen |
| Desire | ↑ | DiscoveryCaution | -0.20 | 0 | 100 | Higher desire → less vigilant about discovery risk |
| Restraint | ↑ | DiscoveryCaution | +0.30 | 0 | 100 | More restraint → more careful about being caught |
| Restraint | ↑ | Exhibitionism | -0.20 | 0 | 100 | More restraint → less comfort being seen |
| Restraint | ↑ | PostEncounterGuilt | +0.15 | 0 | 100 | More restraint → more guilt afterward |
| SelfRespect | ↑ | DiscoveryCaution | +0.20 | 0 | 100 | Higher self-respect → more careful about consequences |
| Loyalty | ↑ | EmotionalEngagement | +0.20 | 0 | 100 | Higher loyalty → more emotional attachment carries into encounter |
| Loyalty | ↑ | PostEncounterGuilt | +0.25 | 0 | 100 | Higher loyalty → more guilt after crossing the line |

**Husband drift rules** (6 rules):

| Stat | Direction | Dimension | Slope | Floor | Ceiling | Narrative intent |
|------|-----------|-----------|-------|-------|---------|-----------------|
| Dominance | ↓ (negative slope) | Acceptance | -0.35 | 0 | 100 | Less dominant → more accepting of situation |
| Dominance | ↓ | Voyeurism | -0.25 | 0 | 100 | Less dominant → more inclined to watch |
| Dominance | ↓ | Participation | -0.20 | 0 | 100 | Less dominant → more willing to participate passively |
| Dominance | ↓ | Encouragement | -0.25 | 0 | 100 | Less dominant → more likely to encourage |
| SelfRespect | ↓ | Acceptance | -0.20 | 0 | 100 | Lower self-respect → more accepting of situation |
| SelfRespect | ↓ | Encouragement | -0.20 | 0 | 100 | Lower self-respect → more likely to encourage |

**OtherMan**: No drift rules. `GetRules("OtherMan")` returns empty list.

---

## Modified Interfaces

### `IBehavioralFrameGenerator` (modified)
**File**: `DreamGenClone.Application/StoryAnalysis/Abstractions/IBehavioralFrameGenerator.cs`

New signature:
```csharp
Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
    IReadOnlyDictionary<string, string> characterEncounterProfileIds,
    IReadOnlyList<ScenarioCharacter> characters,
    IReadOnlyDictionary<string, CharacterStatProfileV2>? characterRuntimeStats = null,
    CancellationToken cancellationToken = default);
```

The new `characterRuntimeStats` parameter is optional (default null) to preserve all existing call sites that do not yet pass runtime stats.

---

## State Transitions

### RuntimeEncounterStats Lifecycle

```
null (no runtime state)
    │
    ├── [first stat delta in any session]
    │       → Initialize from CharacterProfile.EncounterStats (if bound)
    │       → or from BehavioralDimensionCatalog defaults (50) if no profile bound
    │       → Apply drift for the triggering delta
    │
    ├── [subsequent stat deltas]
    │       → Apply drift rules, clamp to floor/ceiling
    │
    ├── [profile rebind — new CharacterProfile bound to character]
    │       → Reset: overwrite with new CharacterProfile.EncounterStats
    │       → (prior drift discarded)
    │
    ├── [session end]
    │       → Serialised into CharacterSnapshotsJson (persists cross-session)
    │
    └── [session resume]
            → Deserialised from CharacterSnapshotsJson
            → Used immediately on first continuation (no delta needed)
```

---

## Database Impact

**No schema changes required.**

`RuntimeEncounterStats` serialises as a JSON property inside the existing `CharacterSnapshotsJson` TEXT column in `RolePlayV2AdaptiveStates`. Existing rows remain valid; missing `RuntimeEncounterStats` deserialises as null and is lazily initialised on first use.

The only DB data change is removal of Tension and Connection stat values from seeded rows in:
- Keyword category stat names
- Theme stat affinity entries
- Semantic event stat mapping entries
- Character stat preset default values
