# Feature Specification: Attractiveness Tier Catalog

**Feature Branch**: `079-attractiveness-tier-catalog`
**Created**: 2026-08-12
**Status**: Draft
**Input**: User description: "Attractiveness Tier Catalog — map the PhysicalAttributes.AttractivenessRating (1–10) on character profiles to effect-based tier prose (five bands: Striking / Attractive / Average / Plain / Repelling) so the model renders attractive characters as magnetic in narrative, injected into the appearance block, applying to all characters and genders, with no cross-character stat coupling."

## Problem Statement

A character's `AttractivenessRating` (1–10) currently renders as a bare number in the prompt appearance block (e.g. `Attractiveness: 10/10`). That number is a dead signal — the model has no narrative meaning attached to it, so a 10/10 character is written as passively "present" rather than magnetic. This was observed in session `44d9af9f`, where a 10/10 character was written as "not pushing, just present" with no awareness of his own magnetism. This feature attaches **effect-based prose** (physical descriptors + behavioral cues describing how others react) to each rating band, so the number becomes a narrative engine.

The prose is **injected state only** — it appears in the character's own appearance block, which all scene actors already see. Other characters' reactions flow through the narrative (emergent), exactly as the engine already handles the seduction archetype and role context. **No cross-character stat coupling** is introduced.

## User Scenarios & Testing *(mandatory)*

### User Story 1 – A striking character is written as magnetic (Priority: P1)

A player runs a roleplay session where a present character has an attractiveness rating of 9 or 10. Instead of a bare `Attractiveness: 10/10`, the character's appearance block in the assembled prompt carries the "Striking" tier prose — a physical descriptor plus behavioral cues (people turn heads, get flustered or nervous, attention follows them into a room, presence is felt before they speak). The model then writes that character as magnetic rather than passively present, and other characters' reactions appear in the scene.

**Why this priority**: This is the core of the feature — it converts the currently-dead number into narrative. Without it, attractive characters keep being written as passively present (the observed defect).

**Independent Test**: Start a session with a present character whose attractiveness is 10, open the prompt/debug log, and confirm the appearance block contains `Attractiveness: 10/10 — Striking: <prose>` where the prose has both a physical descriptor and a behavioral-cue sentence.

**Acceptance Scenarios**:

1. **Given** a present character with `AttractivenessRating = 10`, **When** the appearance block is assembled, **Then** the line reads `Attractiveness: 10/10 — Striking: <tier prose>`.
2. **Given** a present character with `AttractivenessRating = 9`, **When** the appearance block is assembled, **Then** the same "Striking" band prose is appended.
3. **Given** a scene with a striking character present, **When** every scene actor's prompt is assembled, **Then** every actor's prompt contains that character's appearance block including the tier prose (so reactions can flow through the narrative).
4. **Given** a character with `AttractivenessRating = 5`, **When** the appearance block is assembled, **Then** the line reads `Attractiveness: 5/10 — Average: <tier prose>` with the Average band prose.

---

### User Story 2 – One attractiveness scale for every character, all genders (Priority: P1)

The attractiveness tier rendering applies identically to **all** character roles — persona, Husband, Wife, OtherMan, and NPCs — and to **all** genders, using the same 1–10 scale. The prose must be gender-neutral so it works for male, female, and any other gender presentation without assuming a body type or gender. A male 9–10 and a female 9–10 both render "Striking".

**Why this priority**: The user explicitly decided this is NOT OtherMan-only — the persona, Husband, Wife, and NPCs all use the identical scale. A gender- or role-specific implementation would be a defect.

**Independent Test**: Create characters across roles and genders with the same rating (e.g. a male OtherMan and a female Wife, both 9), start a session, and confirm both appearance blocks render the same "Striking" band with gender-neutral prose.

**Acceptance Scenarios**:

1. **Given** characters of different roles (persona, Husband, Wife, OtherMan, NPC) each with the same rating, **When** their appearance blocks are assembled, **Then** each renders the same tier label and band prose.
2. **Given** male and female characters with the same rating, **When** their appearance blocks are assembled, **Then** both use the same band and the prose contains no gender- or body-type-specific assumptions.
3. **Given** the persona's own appearance block is assembled, **When** the persona has an attractiveness rating, **Then** it also carries the tier prose (same rendering path as other characters).

---

### User Story 3 – Unset or invalid attractiveness stays invisible (Priority: P1)

When a character has no attractiveness rating, or the stored rating is outside the valid 1–10 range, no attractiveness line is rendered — preserving current behavior and preventing any malformed data from reaching the prompt.

**Why this priority**: This guards the prompt against regressions and malformed data. It is a precondition for safe rollout to all characters.

**Independent Test**: Create a character with no attractiveness value and a character with an out-of-range value (e.g. 0 or 11); assemble both appearance blocks and confirm no attractiveness line appears in either.

**Acceptance Scenarios**:

1. **Given** a character with no `AttractivenessRating` set, **When** the appearance block is assembled, **Then** no attractiveness line appears.
2. **Given** a character with an out-of-range rating (below 1 or above 10), **When** the appearance block is assembled, **Then** no attractiveness line appears and the block renders without error.

---

### User Story 4 – A character who knows they're striking writes differently (Priority: P2)

*(Optional, deferred)* The behavioral self-awareness text for a character may include their own attractiveness, framed by the existing awareness-level mechanism: a striking character with high awareness knows their looks shape how others behave around them and writes accordingly; a character with low awareness does not dwell on their own looks. When no awareness level applies, the current output is unchanged.

**Why this priority**: This is a refinement that deepens a character's own POV of their presence. The P1 stories deliver the primary value (others' reactions flow through narrative); this story is additive and can ship later without rework.

**Independent Test**: With the feature enabled, inspect the behavioral self-awareness text for a striking character at high awareness vs. low awareness and confirm the framing differs; confirm no awareness level produces the current (unchanged) text.

**Acceptance Scenarios**:

1. **Given** a character with high awareness and a 9–10 attractiveness rating, **When** the behavioral self-awareness text is built, **Then** the framing reflects that the character is aware of their magnetism and that it shapes interactions.
2. **Given** a character with low awareness and a 9–10 attractiveness rating, **When** the behavioral self-awareness text is built, **Then** the framing indicates the character does not dwell on their own looks.
3. **Given** no awareness level is present, **When** the behavioral self-awareness text is built, **Then** the output is unchanged from today (no attractiveness framing added).

---

### Edge Cases

- **Rating is `null`**: The attractiveness line must be omitted entirely (current behavior), not rendered with any default or placeholder.
- **Rating out of range** (0, 11, negative): The line must be omitted and must not cause an error. The catalog's resolve operation returns no tier for out-of-range values.
- **Boundary ratings** (1, 3, 5, 7, 9): Each must resolve to exactly the correct band — 1→Repelling, 3→Plain, 5→Average, 7→Attractive, 9→Striking — with no band overlap.
- **Adjacent bands**: Ratings 6 vs. 7 and 8 vs. 9 must not bleed across band boundaries (band ranges are non-overlapping).
- **Gender neutrality**: Prose must not assume the character's gender or body type, since the same prose renders for male, female, and other gender presentations across all roles.
- **Persona's own block**: The persona's appearance block carries behavioral cues about how *others* react — this must not conflict with the persona's self-view instruction; the same block already shows the persona's other attributes to the model today.
- **Empty appearance block**: If attractiveness is the only populated field, the block still renders with the attractiveness line; if no fields (including attractiveness) are populated, the block is omitted as today.
- **Existing tests**: Appearance-block tests that assert the current block shape must continue to pass — the tier prose is strictly additive after the `n/10`.
- **No fallback prose**: If the rating is set but the catalog cannot resolve a tier (data anomaly), the line is omitted — the formatter must never inject substitute/hardcoded prose (repo no-fallback rule).
- **No stat coupling**: The feature must introduce zero changes to any stat, willingness formula, or gate threshold — the only change is the text injected into the appearance block.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a static, code-authored attractiveness tier catalog consisting of exactly five non-overlapping bands that together cover ratings 1–10: Striking (9–10), Attractive (7–8), Average (5–6), Plain (3–4), Repelling (1–2).
- **FR-002**: Each band MUST have a short label (Striking / Attractive / Average / Plain / Repelling) and effect-based prose. The prose for every band MUST include **at least one physical descriptor** AND **at least one behavioral-cue sentence** describing how others react to the character (e.g. turns heads, people get flustered, attention follows them, presence is felt, people avoid eye contact).
- **FR-003**: The catalog MUST expose a single resolve operation that maps a rating to exactly one band for any rating 1–10, and returns no tier for a `null` or out-of-range rating.
- **FR-004**: When a character has an attractiveness rating that resolves to a tier, the appearance block MUST render the line as `Attractiveness: n/10 — <Label>: <tier prose>` (prose appended after the numeric value).
- **FR-005**: When the rating is `null` or out of range, the attractiveness line MUST be omitted (current behavior preserved).
- **FR-006**: The tier prose MUST come exclusively from the catalog's resolve operation. The formatter MUST NOT contain fallback or hardcoded substitute prose; if no tier resolves, the line is omitted. (This is optional character data — omitting the line when absent is intended behavior and does not conflict with the repo's "fail fast on required RP config" rule, since attractiveness is not required configuration.)
- **FR-007**: The attractiveness tier rendering MUST apply uniformly to ALL character roles (persona, Husband, Wife, OtherMan, NPC) and all genders using the same 1–10 scale, including the persona's own appearance block.
- **FR-008**: The band prose MUST be gender-neutral and must not assume a specific gender or body type.
- **FR-009**: The feature MUST NOT introduce cross-character stat coupling: no stat deltas, no willingness-formula changes, no gate-threshold changes, and no new mechanics. Other characters' reactions to attractiveness MUST flow through the narrative only (the injected appearance block already visible to all scene actors).
- **FR-010** *(P2, optional)*: The behavioral self-awareness text MAY incorporate a character's attractiveness tier, gated behind the existing awareness-level mechanism (high awareness → aware of and shaped by their magnetism; low awareness → does not dwell on it). When no awareness level applies, the output MUST remain unchanged from today.
- **FR-011**: The feature MUST NOT require any database schema change or migration. It MUST use the existing attractiveness field as already stored; no new persisted store is introduced (the SQLite-default persistence policy is intentionally not applicable here — documented exception: this is a static, code-defined catalog with no runtime-persisted data).
- **FR-012**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices; any new diagnostics (e.g. a rating present but unresolvable) MUST be structured and actionable.
- **FR-013**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities *(include if feature involves data)*

- **AttractivenessTier (band)**: A single rating band defined by a rating range (min–max, within 1–10), a short label, and effect-based prose containing both a physical descriptor and behavioral-cue sentences. Five bands exist: Striking, Attractive, Average, Plain, Repelling.
- **AttractivenessTierCatalog**: The static collection of all five bands plus the resolve operation that maps a rating (1–10) to exactly one band, and returns no tier for null/out-of-range ratings. It is a fixed, code-defined reference (not user-editable data), analogous to the existing physical-attributes catalog.
- **PhysicalAttributes.AttractivenessRating (existing)**: The `int?` (1–10) input signal stored on each character's profile, already persisted in existing payload columns. The feature reads this value but adds no new data or storage.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every rating in 1–10 resolves to exactly the correct band (10 ratings → 10 correct mappings), verified by automated test.
- **SC-002**: 100% of the five band prose entries contain both at least one physical descriptor AND at least one behavioral-cue sentence, verified by automated test.
- **SC-003**: In a live session with a 9–10 character present, the character's appearance block in every scene actor's assembled prompt contains the tier prose, verifiable in the prompt/debug log.
- **SC-004**: Characters with null or out-of-range ratings produce no attractiveness line, and all pre-existing appearance-block tests pass unchanged (zero regressions).
- **SC-005**: The feature introduces zero changes to stats, willingness behavior, or gate thresholds — verified by the test suite and the absence of any coupling code paths.
- **SC-006** *(P2)*: For a striking character, high awareness produces self-awareness framing that reflects their magnetism, and low awareness produces framing that does not dwell on it — both verifiable by prompt inspection.

## Assumptions

- The catalog is **static and code-defined** — not per-scenario configurable. Prose is authored once in code.
- Band prose is authored to be **gender-neutral** so it applies across all genders and roles without edits.
- The **persona's own appearance block** carries the tier prose too (it uses the same rendering path) — this is desired, not a defect.
- The tier prose is **strictly additive** after the `n/10` value; no existing appearance fields are removed or restructured.
- The **P2 behavioral self-awareness integration is deferred**; the P1 stories must not depend on it.
- No **new persisted data or DB migration** is required — the existing `int?` attractiveness field is reused as-is. The SQLite-default persistence requirement does not apply to this feature (documented exception in FR-011).
- Attractiveness is **optional character data**, not required RP configuration — so omitting the line when the rating is absent is intended behavior, not a forbidden fallback.
- The observed narrative defect (session `44d9af9f`) is expected to be resolved by the injected prose alone; no additional prompt-instruction work is in scope.
