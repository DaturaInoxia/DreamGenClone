# B-166 — Prompt element ownership, POV framing, and required-output contract

**State:** designed (intent captured 2026-10-19; decisions D1–D4 open)
**Supersedes:** the implicit rules currently spread across `StepPromptElementPlan`, `SceneImagePovProjection`,
the four canonical system prompts, and the comments in `ReferenceBindingPromptRemoval`.
**Refined by:** [character-reference-pov-spec.md](./character-reference-pov-spec.md) (2026-10-19 braindump
analysis). Where the two disagree — notably §3.2's "remove all room prose", which the refinement corrects to
"remove the geometry, keep a location *type* anchor" — the refinement wins.

---

## 1. Problem

A Composition's prompt is currently the product of four independent layers that each decide, separately,
what text an image "takes over":

1. `ReferenceBindingPromptRemoval` + `StepPromptElementPlan` (which brief elements a bound image removes),
2. `SceneImagePovProjection` + the canonical prompt builders (how the frame is described),
3. four family system prompts (each with its own required element order),
4. an LLM pre-processor that decides what of the above actually reaches the final prompt.

Because each layer has its own rule, the same request produces contradictory prompts, and fixing one layer
is undone by another. Measured/reported failures (2026-10-19/20):

- A bound location image produced a prompt that described the room ("corrugated metal walls, wooden
  floorboards showing cracks, pitched roof") and almost nothing about the people.
- Character frame placement (LEFT/CENTER/RIGHT) appeared only sometimes: it is present in the request in
  every case, but the pre-processor drops it whenever the room prose is still present (the room consumes
  the prompt budget).
- Binding face/body/wardrobe references appeared to "remove" a character's placement. Nothing removes
  it; the pre-processor stops stating it. The operator cannot tell an input change from a model choice.
- Removing a location reference did not appear to restore the room text, because the generated prompt is
  not invalidated when bindings change — the old prompt stays on screen and keeps being edited/rendered.
- Lighting could not be overridden by text once a room image was bound, so the same room could not be
  rendered at a different time of day.

**The systemic cause:** required content is *requested* of an LLM and never *enforced*. Any input fix is
advisory until the output is checked.

## 2. Intent (operator's words, normalised)

1. **The room is the location reference image.** When a location image is bound, the prompt says almost
   nothing about the room; it spends its text on the people.
2. **The room is text when there is no location image.** Removing the location reference restores the
   room text in full.
3. **Lighting is text-controlled.** The same room reference is reused at different times of day; the
   prompt (time of day / lighting) changes the light without changing the image.
4. **People and their positions are always described in text.** Appearance, stance, action, clothing, and
   each person's placement in the frame (LEFT/CENTER/RIGHT, front/back/depth).
5. **No reference image may remove a person's placement.** Face, body, wardrobe and pose references take
   over appearance/clothing/stance only.
6. **POV is a framing dimension.** Omniscient is the narrative external view with exact positions;
   a character POV is that character's own eyes (what that camera sees at that eye height).
7. **What the request states is what the prompt must contain.** If the request says where each person
   is, the finished prompt says it too — or the job fails and says what is missing.

## 3. Design

### 3.1 One ownership matrix (the single decision path)

Every prompt element has exactly one owner for a given request, and every layer reads this table.

| Element | Owner when a reference is bound | Owner otherwise | Text rule |
|---|---|---|---|
| Room geometry — walls, floor, surfaces, fixtures, objects, environment | Location image | Text | Image owns ⇒ all room-carrying brief elements are removed (see 3.2). Text owns ⇒ full room prose. |
| **Lighting** | **Text (time of day) by default**, unless D1 selects "from the reference" | Text | Never removed by a location image under the default. "From the reference" removes it explicitly. |
| Time of day | Text (narrative anchor) | Text | Never removed. |
| Mood | Text | Text | Never removed. |
| Character appearance | Face / body image | Text | Image owns ⇒ appearance line removed (existing rule). |
| Character clothing | Wardrobe image | Text | Image owns ⇒ clothing is NAMED AND WORN, never silently dropped. |
| Character stance | Pose image | Text | Image owns ⇒ stance prose removed (existing rule). |
| **Character frame placement** | **Text — ALWAYS** | Text | No reference may remove or suppress it. |
| Character action / people count | Text — ALWAYS | Text | Never removed. |

The matrix is implemented once (`StepPromptElementPlan` for removals, `SceneImagePovProjection` for the
frame) and the system prompts must not restate a conflicting rule.

### 3.2 Room-carrying elements removed when the location image owns the room

`frozenState.location`, `.environment`, `.objects`, `.visualDescription`, `.continuityState`,
`moment.frozenState`, `moment.compositionRationale`, each character's `physicalLocation`, and both
continuity blocks' `location`, `objectStates`, `characterStates`, `stateSummary`, and the blocking
`rationale` prose.

**Not removed:** `frozenState.lighting` (see 3.3), `.timeOfDay`, `.mood`, `moment.visibleAction`,
character `position` / `actionOrObservation` / `sightline` / `clothing`, and `blocking` subjects/camera.

### 3.3 Lighting (D1)

- `frozenState.lighting` is text-owned by default even when a location image is bound: the room's
  geometry comes from the image, its light comes from the moment (time of day).
- A persisted setting on the composition step selects the owner:
  - `FromBrief` (default) — the brief's lighting is written.
  - `FromReference` — the location image's own lighting is used; the lighting text is removed.
- The setting is UI-backed and persisted with the composition's settings (no code-only default).

### 3.4 POV framing (already implemented; written down here as the contract)

- **Omniscient:** external third-person camera; every in-scene character in frame; each person's exact
  LEFT/CENTER/RIGHT position and foreground/midground/background depth stated; camera angle/shot from
  the brief's blocking.
- **Participant POV:** first-person camera at that character's recorded stance/eye height, aimed along
  that character's recorded sightline; only the cast that character's `visibleCharacterNames` records is
  in frame; everyone else is explicitly out of frame; the viewpoint is never depicted.
- The projection is resolved from the **compiled brief** (the world), never from the stripped prompt
  payload; overrides only suppress what the text restates.

### 3.5 Required-output contract and enforcement (D2)

After the pre-processor returns a prompt, it is validated against the *request*, not against itself:

- every depicted person: gender + short appearance + frame placement (when the projection states one) +
  clothing state;
- no room geometry/objects/lighting description when the room (or lighting, per D1) is image-owned;
- room description present when text owns the room;
- camera/POV consistent with the projection; no person the projection puts out of frame.

Enforcement (recommended): validate → one corrective retry that names the missing element → if still
missing, fail the prompt record with a diagnostic naming the element and the person. The diagnostic is
what turns "the prompt looks wrong" into a fixable, visible fault.

### 3.6 Staleness (D3)

The generated prompt is an artifact of a specific request (bindings + overrides + POV + frame). Changing
any of those marks the draft **stale**: the UI says so, and the stale prompt must not be silently
rendered. Regeneration is required. Removing a reference therefore visibly restores the text it owned
once the prompt is regenerated.

## 4. Acceptance tests

1. Location image bound, lighting default → prompt contains **no** room geometry/objects, **does** contain
   the brief's lighting; every person's placement is present.
2. Location image bound, lighting `FromReference` → prompt contains no lighting text.
3. No location image → room prose is present in full (regenerate after unbinding), lighting present.
4. No references, each POV → every person's LEFT/CENTER/RIGHT + depth is present in the prompt.
5. Face + body + wardrobe + pose bound → appearance/clothing/stance prose suppressed or named-worn per the
   matrix, and every person's placement is still present.
6. Omniscient vs Dean vs Becky on the same moment → three different frames (camera height/aim, in-frame
   cast), and the Omniscient prompt states exact positions.
7. Validator: a deliberately placement-less pre-processor output fails with a diagnostic naming the
   person and the missing element; one corrective retry happens first.
8. Staleness: unbinding a reference marks the draft stale and blocks rendering until regenerated.

## 5. Open decisions

- **D1 — lighting default:** `FromBrief` (recommended; matches "same room, different time of day") or
  `FromReference`.
- **D2 — enforcement:** validate + one retry + hard fail (recommended), or deterministic append of the
  required clauses to the generated prompt, or both.
- **D3 — staleness UX:** keep the text but mark it stale and block render (recommended), or clear the
  generated prompt when the request changes.
- **D4 — non-goals to confirm:** no new model/provider; no second prompt pipeline; the pre-processor
  remains the prose writer (we validate its output rather than replace it).

## 6. Out of scope

- Changing the enrichment contract or the beat/moment pipeline.
- Reference-image analysis quality (B-160) and staging-profile editing (B-157/B-162) beyond reading their
  results.
