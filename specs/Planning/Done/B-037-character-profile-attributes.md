# B-037 — Character Profile Attributes Expansion

Extended non-physical attributes for Character and Persona templates.
These complement the existing physical attributes (B-025) and adaptive stats.

---

## Proposed Fields

### Tier 1 — High value (directly shapes LLM behavior and dialogue)

| Field | Type | Notes |
|---|---|---|
| Personality traits | Free text | How the character thinks, speaks, and reacts — most impactful single field for consistent voice |
| Turn-ons | Free text | Drives positive intimate scene behavior and reactions |
| Turn-offs | Free text | Drives resistance, discomfort, and refusal framing |
| Occupation | Short text (preset + custom) | Grounds vocabulary, social context, scheduling excuses |
| Speech style / mannerisms | Free text | Word choice, accent, verbal tics — gives each character a distinct voice |
| Secrets / hidden desires | Free text | Creates narrative tension; LLM can surface these gradually across arcs |

### Tier 2 — Medium value (useful context)

| Field | Type | Notes |
|---|---|---|
| Hobbies / interests | Free text | Small talk, scene props, credible common ground between characters |
| Background | Free text | Backstory the LLM can reference for motivation and emotional reactions |
| Marital status | Preset + custom | Affects loyalty/guilt/stakes framing (e.g. Married, Separated, Divorced, Single, Widowed) |
| Sexual experience level | Preset | Complements the sexual skill stat — narrative framing vs. mechanical value. Options: Inexperienced, Limited, Average, Experienced, Very experienced |
| Relationship to persona | Free text | Richer than the Role/Relation fields — e.g. "ex from college, still carries a torch" |
| Emotional state at scene start | Preset | Seeds the opening mood without the LLM guessing. Options: Nervous, Eager, Conflicted, Reluctant, Confident, Playful, Distracted, Sad |

### Tier 3 — Lower priority but solid

| Field | Type | Notes |
|---|---|---|
| Body language style | Preset | How they carry themselves. Options: Reserved, Neutral, Expressive, Seductive, Guarded, Relaxed |
| Kinks / fetishes | Free text | More specific than turn-ons; explicit narrative preferences |
| Motivation in this scenario | Free text | Per-scenario goal, distinct from Background — what does this character want from this arc? |

---

## Implementation Scope

- Domain: add new fields to `Character` (scenario characters) and `TemplateDefinition` (persona/character templates)
- Persistence: fields serialised into existing `PayloadJson` columns — no schema migration needed
- UI: new collapsible section "Character Profile" in `PhysicalAttributesEditor.razor` (or a parallel `CharacterProfileEditor.razor` component)
- Prompt injection: `PhysicalAttributesFormatter.cs` (or a new `CharacterProfileFormatter.cs`) formats the populated fields into a block injected alongside the existing appearance block in `RolePlayContinuationService.BuildPromptAsync`
- Clone helpers: all four `ClonePhysicalAttributes`-style helpers updated to include new fields
- Backwards compatibility: all new fields are optional; existing sessions and characters are unaffected

---

## Open Questions

- Single editor component or separate `CharacterProfileEditor.razor` alongside the physical attributes editor?
- Preset lists for marital status, sexual experience, emotional state, and body language — confirm or adjust options before implementation
- Should "Motivation in this scenario" live on the Scenario's `Character` only (not on templates)?
