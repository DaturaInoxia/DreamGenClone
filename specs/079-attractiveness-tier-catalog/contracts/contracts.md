# Contracts: Attractiveness Tier Catalog

**Branch**: `079-attractiveness-tier-catalog` | **Date**: 2026-08-12

This document defines the public-surface contracts introduced by this feature: the Domain catalog types, the updated formatter output contract, and the (deferred) self-awareness integration contract.

---

## Contract 1 — `AttractivenessTier` record

**File**: `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs` (top of file)  
**Type**: `public sealed record AttractivenessTier(int Min, int Max, string Label, string Prose)`

```csharp
/// <summary>
/// A single attractiveness rating band defined by a rating range (within 1–10),
/// a short label, and effect-based prose containing a physical descriptor AND
/// behavioral-cue sentences (how others react).
/// </summary>
public sealed record AttractivenessTier(int Min, int Max, string Label, string Prose);
```

---

## Contract 2 — `AttractivenessTierCatalog`

**File**: `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs`  
**Type**: `public static class AttractivenessTierCatalog`

```csharp
/// <summary>
/// Code-defined catalog of the five attractiveness bands (Striking / Attractive /
/// Average / Plain / Repelling), covering ratings 1–10. Single source of truth for
/// band labels and effect-based prose. Analogous to PhysicalAttributesCatalog.
/// </summary>
public static class AttractivenessTierCatalog
{
    /// <summary>All five bands, ordered low→high, non-overlapping, covering 1–10.</summary>
    public static readonly IReadOnlyList<AttractivenessTier> All;

    /// <summary>
    /// Maps a rating (1–10) to exactly one band. Returns null for null or
    /// out-of-range ratings (no tier → the formatter omits the line).
    /// </summary>
    public static AttractivenessTier? Resolve(int? rating);
}
```

**Resolve contract** (deterministic, pure):

| Rating | Result |
|--------|--------|
| 10, 9 | Striking |
| 8, 7 | Attractive |
| 6, 5 | Average |
| 4, 3 | Plain |
| 2, 1 | Repelling |
| `null`, 0, 11, any other out-of-range | `null` |

### Band prose (author-once, gender-neutral, effect-based)

Each `Prose` **must** contain **≥1 physical descriptor** and **≥1 behavioral-cue sentence** (SC-002).

| Min–Max | Label | Prose (draft — review before finalizing) |
|---------|-------|-------------------------------------------|
| 9–10 | **Striking** | "Features that command attention — striking symmetry, a face and body that draw the eye and linger in memory. People turn to look when they enter a room, get flustered or nervous up close, and feel their presence before a word is spoken; attention follows them wherever they go." |
| 7–8 | **Attractive** | "Genuinely good-looking with pleasant, well-kept features that read as naturally appealing. Others give lingering looks and easy smiles, act warmer and more attentive than usual, and feel a noticeable pull toward their company." |
| 5–6 | **Average** | "Unremarkable, ordinary features that fit comfortably into any crowd. People register them without particular interest; interactions run neutral, drawing neither special attention nor avoidance." |
| 3–4 | **Plain** | "Forgettable features that make little impression — unremarkable, slightly off-putting, easily overlooked in a room. Others rarely volunteer attention; their presence registers as neutral-to-negative." |
| 1–2 | **Repelling** | "Strikingly unattractive — neglected or actively unappealing features that repel rather than invite. People avoid eye contact and keep their distance; their presence works against them, and attraction is actively absent." |

### Prose cue lexicon (for automated prose-contract tests)

- **Physical descriptor keywords**: `features`, `symmetry`, `face`, `body`, `well-kept`, `ordinary`, `unremarkable`, `forgettable`, `neglected`, `unappealing` (each band has at least one).
- **Behavioral-cue sentence markers**: how others react — `turn to look`, `flustered`, `nervous`, `attention follows`, `presence`, `lingering looks`, `smiles`, `warmer`, `pull`, `interest`, `avoid eye contact`, `distance`, `avoidance` (each band has at least one).
- Tests assert "at least one physical-descriptor token **and** at least one behavioral-cue token per band" so prose can be reworded without breaking the contract.

---

## Contract 3 — `PhysicalAttributesFormatter.FormatBlock` output (updated)

**File**: `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs`  
**Type**: `internal static class PhysicalAttributesFormatter`

```csharp
/// <summary>
/// ...existing contract unchanged for all other fields...
/// AttractivenessRating resolves to a tier → rendered as "Attractiveness: n/10 — <Label>: <tier prose>".
/// Rating null or out of range → the attractiveness entry is omitted (current behavior preserved).
/// Tier prose comes exclusively from AttractivenessTierCatalog.Resolve — no fallback text.
/// </summary>
internal static string FormatBlock(PhysicalAttributes? attrs, string? gender = null)
```

**Output change** (only the attractiveness entry; field order and all other entries unchanged):

```
Before:  Appearance — ...; Attractiveness: 10/10; ...
After:   Appearance — ...; Attractiveness: 10/10 — Striking: Features that command attention — striking symmetry, ...; ...
```

**Behavior table**:

| `AttractivenessRating` | Rendered line |
|------------------------|---------------|
| `10` | `Attractiveness: 10/10 — Striking: <prose>` |
| `9` | `Attractiveness: 9/10 — Striking: <prose>` |
| `7`–`8` | `Attractiveness: n/10 — Attractive: <prose>` |
| `5`–`6` | `Attractiveness: n/10 — Average: <prose>` |
| `3`–`4` | `Attractiveness: n/10 — Plain: <prose>` |
| `1`–`2` | `Attractiveness: n/10 — Repelling: <prose>` |
| `null` | line omitted |
| `0`, `11`, negative | line omitted (no fallback, no error) |

**No-fallback guarantee**: the only prose source is `AttractivenessTierCatalog.Resolve(rating)`. There is no substitute/hardcoded text branch (repo no-fallback hard rule; FR-006). Omitting the line when the rating is absent is *intended* (attractiveness is optional character data).

**Injection surface (unchanged)**: the appearance block flows through the existing 4 call sites — `CharacterDataSlot` (Slot 5, Zone B; persona + present characters), `RolePlayContinuationService` (persona + NPC blocks), `InteractionRetryService` (retry prompt). All actors in the scene already see every present character's block, so reactions are emergent (FR-009 — injected state only).

---

## Contract 4 — `BuildSelfAwarenessText` attractiveness framing (P2, DEFERRED)

**File**: `DreamGenClone.Web/Application/RolePlay/IntimateBehavioralTextBuilder.cs`  
**Type**: `internal static string? BuildSelfAwarenessText(PhysicalAttributes attrs, string gender, int? awarenessLevel = null, string? name = null)`

**Scope**: NOT implemented in P1. Locked contract for the later P2 slice (FR-010):

- **Gated**: attractiveness framing is appended **only when `awarenessLevel` is present**. When `awarenessLevel` is `null`, output is byte-for-byte unchanged from today (existing call sites pass `null` for scenario characters).
- **Tier-aware framing** (uses the same `AttractivenessTierCatalog.Resolve`):
  - `awarenessLevel >= 70` (high): frames that the character is aware of their magnetism and that it shapes how others behave around them.
  - `awarenessLevel <= 30` (low): frames that the character does not dwell on their own looks.
  - middle (31–69): quiet awareness.
- Uses the existing awareness thresholds already present in the method (≥70 / ≤30 / else).
- No change to `BuildBehavioralRules`, `BuildPartnerPerspectiveText`, or `BuildPartnerPreEncounterText`.
