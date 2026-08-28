# B-079: Attractiveness Tier Catalog — Narrative Prose for the 1–10 AttractivenessRating

**State**: `designed` (plan persisted, pending implementation confirmation)
**Priority**: medium
**Scope**: medium
**Date**: 2026-08-12

---

## TL;DR

The `PhysicalAttributes.AttractivenessRating` (1–10) currently renders as a bare number (`Attractiveness: 10/10`) in the appearance block — a dead signal the model can't turn into narrative. This plan adds a **static code-defined tier catalog** mapping each rating band to **effect-based descriptive prose** (physical + behavioral cues), rendered inline by `PhysicalAttributesFormatter`. The prose applies to **all characters and genders** (persona, Husband, Wife, OtherMan, NPCs), so attraction is **emergent** — a "Striking" character's presence is injected, and other characters' reactions flow through the narrative. **No cross-character stat coupling** — attractiveness never alters any stat, willingness score, or gate.

**User decisions (2026-08-12)**:
1. **Static catalog** — code-defined, not per-scenario configurable.
2. **Physical + behavioral-cue heavy prose** — each band describes both how the character looks AND how people react/behave around them.
3. **Same 1–10 scale for ALL roles and genders** — not OtherMan-only; the persona, Husband, Wife, and NPCs all use the identical scale.

---

## Background (verified in code)

### Current state

- `PhysicalAttributes.AttractivenessRating` is `int?` (1–10), stored as JSON in the existing payload columns (`DreamGenClone.Domain/Templates/PhysicalAttributes.cs`).
- `PhysicalAttributesFormatter.FormatBlock(attrs, gender)` renders it as a single labelled entry: `Attractiveness: 10/10` (`DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs`).
- The full appearance block is injected into every actor's prompt via `CharacterDataSlot` (Slot 5, Zone B) for present characters, and via `RolePlayContinuationService` for the persona/NPC blocks. **All actors in the scene see every present character's appearance block** — verified in session 44d9af9f (Dean's block, including `Attractiveness: 10/10`, appears in Becky's prompt).

### The gap

The model receives `10/10` with zero narrative meaning. It does not know a 10 should make women stare, get flustered, or feel his presence before he speaks. Result (session 44d9af9f): Dean is written as passively "present" — "not pushing, just present, a porch light left on" — with no awareness of his own magnetism or its effect on the Wife. The signal exists but conveys nothing.

---

## Design

### 1. `AttractivenessTierCatalog` (static, Domain layer)

New static class in `DreamGenClone.Domain/Templates/` (alongside `PhysicalAttributesCatalog`), analogous to `BehavioralDimensionCatalog` and `SeductionArchetypeCatalog`.

```csharp
public sealed record AttractivenessTier(int Min, int Max, string Label, string Prose);

public static class AttractivenessTierCatalog
{
    /// <summary>All bands, ordered low→high. Non-overlapping, covering 1–10.</summary>
    public static readonly IReadOnlyList<AttractivenessTier> All;

    /// <summary>Resolve the tier for a rating (1–10). Returns null for out-of-range.</summary>
    public static AttractivenessTier? Resolve(int? rating);
}
```

### 2. Tier bands (physical + behavioral-cue prose)

Five bands, each with a label and effect-based prose combining **physical description** and **behavioral cues** (how people react). Prose is authored once in code.

| Band | Rating | Label | Physical cue | Behavioral cue |
|------|--------|-------|--------------|----------------|
| 1 | 9–10 | **Striking** | Standout features, magnetic symmetry, a body that draws the eye | Turns heads; people get flustered or nervous; attention follows them into a room; presence is felt before they speak; others go out of their way to be near or impress them |
| 2 | 7–8 | **Attractive** | Genuinely good-looking, pleasant features, well-kept | Earns lingering looks and easy smiles; people are warmer and more attentive; noticeable pull |
| 3 | 5–6 | **Average** | Unremarkable, ordinary features | Blends in; draws no particular attention; interactions are neutral |
| 4 | 3–4 | **Plain** | Forgettable features, somewhat off-putting | Draws no notice; presence is neutral-to-negative; people rarely volunteer attention |
| 5 | 1–2 | **Repelling** | Strikingly unattractive, neglected or unappealing features | People avoid eye contact; presence works against them; attraction is actively absent; a face only those who love them could call kind |

**Prose style contract**: Each band's prose MUST include at least one physical descriptor AND at least one behavioral-cue sentence (how others react). This makes the tier a narrative engine, not a label.

### 3. Formatter integration (`PhysicalAttributesFormatter`)

Change the attractiveness line from bare number to number + prose:

```
Attractiveness: 10/10 — Striking: <tier prose>
```

- When `AttractivenessRating` is set, `FormatBlock` appends the tier prose after the `n/10`.
- When the rating is null or out of range, the line is omitted (current behavior).
- The tier prose comes exclusively from `AttractivenessTierCatalog.Resolve(rating)` — no fallback/hardcoded text in the formatter (repo no-fallback rule).

### 4. Optional: `IntimateBehavioralTextBuilder` self-awareness integration

Add attractiveness to the `BEHAVIORAL CONSTRAINT — X's attributes:` prose so the character's own presence is framed. A "Striking" character who *knows* he turns heads writes differently than a "Plain" one who's never been noticed. Gated behind the existing `awarenessLevel` mechanism — the character's self-awareness of their own attractiveness varies.

### 5. No cross-character stat coupling (explicit)

- Attractiveness prose is **injected state only** — it appears in the character's own appearance block, which all scene actors already see.
- No stat delta, no willingness formula change, no gate threshold change, no new mechanic.
- The Wife's reaction to a "Striking" OtherMan is **emergent** — the model writes her noticing him because his presence prose is in her prompt, exactly as it already writes her reacting to the seduction archetype and role context.
- This is the same mechanism the engine already uses everywhere: **state is injected, the model reacts.**

---

## File list

| File | Change |
|---|---|
| `DreamGenClone.Domain/Templates/AttractivenessTierCatalog.cs` | **NEW** — tier record + static catalog + `Resolve(rating)` |
| `DreamGenClone.Web/Application/RolePlay/PhysicalAttributesFormatter.cs` | `FormatBlock` appends tier prose to the `Attractiveness: n/10` line |
| `DreamGenClone.Web/Application/RolePlay/IntimateBehavioralTextBuilder.cs` | Optional — add attractiveness to self-awareness prose |
| `DreamGenClone.Tests/StoryAnalysis/AttractivenessTierCatalogTests.cs` | **NEW** — catalog tests (bands cover 1–10, prose has physical + behavioral cues, Resolve null/out-of-range) |
| `DreamGenClone.Tests/RolePlay/Prompts/PhysicalAttributesFormatterTests.cs` | **NEW/extended** — formatter renders prose for a set rating; omits when null |

## Blast radius

- **Prompt-content change to the appearance block only.** No stat mutation, no semantic pipeline change, no DB schema change (field already exists), no gate/willingness change, no new slot.
- Affects every prompt that carries a character's appearance block (CharacterDataSlot for present characters; persona/NPC blocks). This is intended — the scale is all-roles, all-genders.
- Existing appearance-block tests must still pass (prose is additive after the `n/10`).

---

## Open items / verification

- [ ] Confirm prose authorship for the 5 bands (draft in code, review once).
- [ ] Confirm the `IntimateBehavioralTextBuilder` integration (Layer 4) is in scope for P1 or deferred.
- [ ] Confirm whether the persona's own appearance (player POV) should carry the tier prose too (it uses the same formatter — yes by default).
- [ ] Build web + tests clean; run new catalog + formatter tests.
