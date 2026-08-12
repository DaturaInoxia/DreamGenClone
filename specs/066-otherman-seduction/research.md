# Phase 0 Research: OtherMan Seduction Archetype

**Branch**: `066-otherman-seduction` | **Date**: 2026-08-11

This document resolves every design decision and open technical question raised during plan generation. Each entry records the Decision, Rationale, and Alternatives considered. All decisions align with the repo Hard Rules (no fallbacks for RP engine behavior, fail-fast on missing config, UI-backed persisted config for every RP behavior control).

---

## R1. Where does the `SeductionArchetypeCatalog` live in the layered solution?

**Decision**: `DreamGenClone.Domain/StoryAnalysis/SeductionArchetypeCatalog.cs` — same namespace and project as `SteerRoleIntentCatalog.cs`.

**Rationale**: The archetype catalog is a pure domain concept: it defines named behavioral modes with prose descriptions. It has zero dependencies (no I/O, no services, no config). This is identical to `SteerRoleIntentCatalog` which lives in the same location. The Domain project is the canonical home for code-defined catalogs that don't change at runtime — they're part of the domain model, not configuration data.

**Alternatives considered**:
- *Infrastructure (configuration)*: Rejected — the archetypes are genre analysis findings, not configurable settings. They don't change per-session or per-deployment. Putting them in Infrastructure would violate the dependency direction (Domain concepts shouldn't live in Infrastructure).
- *Application layer*: Rejected — the catalog has no Application-layer dependencies. It's used by both prompt building (Application) and steering option generation (Application), so it belongs in a lower layer both depend on.

---

## R2. How are `SeductionArchetypes` persisted on the `Character` entity?

**Decision**: Add a `List<string> SeductionArchetypes` property to `Character` (in `DreamGenClone.Web/Domain/Scenarios/Character.cs`). Persisted as a JSON array within the existing scenario character JSON blob in SQLite. No new table or column.

**Rationale**: The `Character` entity already stores structured data as JSON within the scenario's `PayloadJson` column in the `Scenarios` table. Existing list-typed properties (`LocationAffinities`, `PhysicalAttributes`) follow this exact pattern — they're serialized/deserialized with `System.Text.Json` as part of the scenario's character list. Adding a `List<string>` property requires no migration, no new table, and no serializer changes (the default JSON serializer handles `List<string>` natively).

The spec (FR-003) explicitly states: "System MUST store seduction archetype assignments on the Character entity within the scenario — NOT in session-scoped state — so the same character behaves consistently across sessions." This JSON-in-SQLite approach satisfies that requirement with zero infrastructure overhead.

**Alternatives considered**:
- *New `CharacterSeductionArchetypes` SQLite table with FK to character*: Rejected — over-engineered for a simple string list. Character entities are already stored as JSON within scenario blobs; introducing a separate table would require scenario-level migrations, FK management, and break the existing `ScenarioCharacter` → `Character` deserialization flow.
- *Session-scoped state (e.g., `RolePlaySession.SeductionArchetypes`)*: Rejected — spec explicitly forbids this (FR-003). Archetype configuration belongs to the character definition, not the session runtime.
- *Enum-based storage (integer flags)*: Rejected — strings are simpler, self-documenting in JSON dumps, and the catalog lookup is a dictionary anyway (case-insensitive). No performance benefit from ints for an 8-entry set.

---

## R3. How is archetype guidance injected into the continuation prompt?

**Decision**: Extend the existing `CharacterDataSlot.AppendCharacterRoleIntents()` method (Slot 5, Zone B). When a character has `Role == "OtherMan"` AND `SeductionArchetypes` is non-empty, append archetype-specific seduction guidance immediately after the role intent text. When `SeductionArchetypes` is empty, only the role-level intent (from `SteerRoleIntentCatalog.GetRoleContext("OtherMan")`) is emitted — no archetype-specific text.

**Rationale**: The spec's architecture decision section recommends extending `CharacterDataSlot` (Option 1) as the simplest approach. The archetype guidance enriches the character's existing role intent — it answers "HOW this OtherMan pursues," which is a natural extension of the role intent which answers "WHAT this OtherMan's narrative job is." Co-locating them in the same section keeps the prompt coherent and avoids introducing a new slot for a small text block (~100-250 chars).

The guidance text is role-gated (FR-007): only applied when `character.Role == "OtherMan"`. No other role receives archetype injection. This check is a simple string comparison in the append method.

**Alternatives considered**:
- *New dedicated `SeductionGuidanceSlot` (Slot X, Zone C)*: Rejected for P1/P2 per spec recommendation. A dedicated slot adds DI registration, a new implementation class, and a new `PromptSlotId` enum entry for ~250 chars of text. The benefit (finer positioning control) doesn't justify the complexity. Can be deferred to a later iteration if isolation proves necessary.
- *Inject via `BehavioralFramesSlot` (Slot 13, Zone C)*: Rejected — behavioral frames are actor-filtered cross-character constraints (e.g., "X avoids Y", "Z watches W"). Archetype guidance is character-specific behavioral style, not an inter-character frame. Different semantic domain.

---

## R4. How does the `SteerRoleIntentCatalog` OtherMan TOWARDS fallback work?

**Decision**: The existing `SteerRoleIntentCatalog.GetRoleContext("OtherMan")` method already returns the role-level narrative job text. This text is updated to reflect research-backed seduction patterns (FR-005). When `CharacterDataSlot.AppendCharacterRoleIntents()` finds an OtherMan character with NO archetypes configured, it emits ONLY the role-level context — same as today. When archetypes ARE configured, it emits the role-level context PLUS the archetype-specific guidance.

The `SteerRoleIntentCatalog.GetIntent("OtherMan", SteerDirection.Towards)` method (used by steering option generation) is also updated with the same research-backed seduction text (FR-005, User Story 2).

**Rationale**: The spec (FR-006) mandates a single fallback path: "The role-level catalog OtherMan TOWARDS intent MUST serve as the fallback when a character has no archetype configured — no other fallback path may exist." The existing architecture already provides this path through `SteerRoleIntentCatalog`. No new fallback mechanism is needed — we update the catalog text and use it when `SeductionArchetypes` is empty.

**Alternatives considered**:
- *Separate fallback text in `SeductionArchetypeCatalog`*: Rejected — duplicates the role-level catalog's purpose and creates two sources of truth for "default OtherMan behavior."
- *Hardcoded fallback in `CharacterDataSlot`*: Rejected — violates the repo Hard Rule against hardcoded defaults. The catalog is the single configured source of truth.

---

## R5. How do archetypes interact with B-077 (gap-aware steering)?

**Decision**: They are complementary and independent — no code integration needed. B-077 adds gap-closing event hints to the steering prompt (e.g., "create events that lower Loyalty"). This feature adds behavioral style guidance to the continuation prompt (e.g., "use Competent + Confidante behaviors"). Both may appear in the same prompt without conflict because they operate on different semantic axes: tactical objective vs. behavioral style.

No code change is needed to "make them compatible." They target different prompt slots (steering options vs. character data) and are composed independently.

**Rationale**: FR-008 states: "The per-character archetype guidance and the B-077 gap-aware steering directive MUST NOT conflict." They don't conflict by construction — they answer different questions for the model.

---

## R6. How is the multi-select UI implemented in the scenario editor?

**Decision**: A new Razor component or sub-section within the existing character settings panel of `ScenarioEditor.razor`. The UI presents the 8 archetypes as toggle-able chips/checkboxes with name + short description. Selection is bound to `Character.SeductionArchetypes` via two-way binding. A "preview" text area below shows the combined prose guidance that will appear in prompts (live-updating as selections change).

This is a P3 item — implemented after P1 (catalog + data model) and P2 (prompt injection) are stable.

**Rationale**: The spec explicitly defines this as a multi-select with live preview (User Story 4). The scenario editor already has per-character settings panels; this extends one of those panels. Blazor Server's two-way binding makes this straightforward — bind checkboxes to `List<string> SeductionArchetypes` and compute the preview from `SeductionArchetypeCatalog.BuildGuidance(archetypes)`.

**Alternatives considered**:
- *Separate page or modal*: Rejected — spec says "updates the existing character settings panel"; a new page adds navigation friction for a small configuration task.
- *Dropdown multi-select (like a tag picker)*: Considered but chips/checkboxes are more discoverable for 8 items and allow the author to read descriptions inline.
