# B-089 — Continuation Settings Redesign: Tempo + Span

**Created:** 2026-08-18
**Status:** `designed` — design finalized 2026-08-19 (Tempo + Span wordings locked in §3.7). Not yet implemented; tasks in §4.
**Backlog:** B-089
**Source:** Full continuation/pacing audit of 2026-08-18 (persisted in `/memories/repo/continuation-settings-audit.md`).

---

## 1. Problem Statement

The Continuation Settings popup (B-082) exposes **9 independent settings** — Pacing, Beat Style, Time Shift, Granularity, Deepening, Scene Presence, Climax Mode, Aftermath, Word Count — resolved through a 4-tier precedence chain (override → theme marker → phase default → hardcoded fallback) and injected as up to 3 separate "HARD CONSTRAINT" lines that all encode the same underlying question: *how fast does time move in this scene?*

The audit found the system **over-complicated in the wrong dimension**:

- Several controls are dead or dampened under default config.
- The default Climax config produces **directly contradictory HARD CONSTRAINTs in the same prompt**.
- There is no single source of truth for "time movement" — it is encoded in 6 different places.
- The user-facing mental model (9 dials) does not match how the engine actually behaves.

The goal: the user should be able to control **flow, pacing, and rhythm easily and bug-free**, without knowing which options work well together.

---

## 2. Audit Findings (Verified 2026-08-18)

### 2.1 What each setting actually does today

| Setting | Marker | Prompt consumer | Engine consumer | Verdict |
|---|---|---|---|---|
| Pacing | `[Pacing:slow\|medium\|fast]` | `FinalInstructionSlot` (Slot 17) | — | ⚠️ Partially works (position 2+ gap) |
| Beat Style | `[BeatStyle:single\|short\|episodic]` | Slot 17 + generic beat cursor | Climax 32-beat cursor | ⚠️ Split brain (override ignored for episodic) |
| Time Shift | `[TimeShift:none\|small\|medium\|large]` | Slot 17 | multi-encounter time-skip machine | 🔴 Contradicts itself in Climax by default |
| Granularity | `[Granularity:micro\|meso\|macro\|montage]` | Slot 17 | — | ⚠️ Dead in multi-turn beats |
| Deepening | `[Deepening:subsequent-actors]` | `TurnContextSlot` (Slot 3) | — | ✅ Works |
| Scene Presence | `[ScenePresence]` | Slot 17 | — | 🔴 Redundant with TimeShift=None |
| Climax Mode | `[ClimaxMode:multi-encounter\|quick-finish]` | — | `RolePlayEngineService` + semantic job | ⚠️ quick-finish dead |
| Aftermath | `[Aftermath:husband-contrast]` | — | `ContinueAsAsync` time-skip phases | ✅ Works |
| Word Count | `[targetwords:small\|medium\|large]` | `WritingStyleSlot` (Slot 18) | — | ⚠️ Profile range inert |

> **Doc caveat:** `specs/B-082-continuation-settings/research/marker-implementation-audit.md` claims TimeShift/BeatScope/ScenePresence/Granularity are "not wired". That is **stale** — B-085 consolidated all four into `FinalInstructionSlot` (Slot 17). They render now. The audit doc predates B-085.

### 2.2 Verified contradictions / dead paths

#### C1 — Pacing is dead for the completing actor, and Fast is mostly dead
`FinalInstructionSlot.WriteAsync` branch order:
1. `pos > 1` → fixed containment line with **hardcoded "Medium"** regardless of `sceneDir.Pacing`:
   ```
   HARD CONSTRAINT — Scene Pacing: Medium pacing — You are a subsequent actor — build on the beat already established this turn. Do not restart or jump past it.
   ```
2. `beatBudget > 1 && !isFinalBeatTurn` → "Stay within the current moment. Do not advance…"
3. else → the real Slow/Medium/Fast text.

With default `BeatScope = Short` (budget 3), **Fast fires only on turn 3** (1 of 3 turns). Position 2/3 — usually the completing actor — gets the hardcoded "Medium" line. The control the user reaches for first is structurally muted.

#### C2 — Beat Style is split-brain
Two independent cursor systems in `RunRolePlayV2PipelinesAsync`:
- **Generic cursor** (B-085): non-episodic themes, budget from `ContinuationMarkerCatalog.GetBeatStyleTurnBudget` (Single=1, Short=3, Extended=5). **Honors `ContinuationOverride.BeatScope`.**
- **Episodic 32-beat Climax cursor**: only when `IsEpisodicBeatStyle(theme, "Climax")`. **Reads the theme marker only — ignores the override.** So the UI "Beat Style = Episodic" override has zero behavioral effect on the Climax beat sheet (B-085 stays open for this exact gap).

#### C3 — Time Shift contradicts itself in Climax by default
`PhaseDefaultTimeShiftMap[Climax] = TimeShiftPolicy.Medium` ("Hours to half a day"). A Climax turn with no markers emits, in the same prompt:
- `HARD CONSTRAINT — Scene Pacing: Stay within the current moment. Do not advance…`
- `HARD CONSTRAINT — Time Shift: Medium — Hours to half a day.`
- `HARD CONSTRAINT — Granularity: Meso — One response covers one step… Do not compress…`

Three HCs saying "stay / don't compress" AND "jump hours ahead" simultaneously. The model picks whichever lands last (Time Shift is emitted after Pacing), so Climax either leaps or stalls based on recency, not user intent.

#### C4 — Granularity is force-overridden in multi-turn beats
`FinalInstructionSlot`:
```csharp
if (beatBudget > 1)
    sb.AppendLine($"HARD CONSTRAINT — Granularity: {sceneDir.Granularity} — One response covers one step of this multi-turn moment. Do not compress the whole moment into this response.");
else
    sb.AppendLine($"HARD CONSTRAINT — Granularity: {sceneDir.Granularity} — {DescribeGranularity(sceneDir.Granularity)}");
```
When `BeatScope = Short` (the default), the user's chosen Granularity label prints but the description is **replaced** with "one step of a multi-turn moment". Micro/Meso/Macro/Montage all read identically under default config.

#### C5 — Scene Presence is redundant with Time Shift = None
- `RequireScenePresence = true` → `HARD CONSTRAINT — Scene Presence: Stay present — no time skip.`
- `TimeShift = None` → `HARD CONSTRAINT — Time Shift: None — No time skip — continue from the exact moment.`

Same semantic, two controls. The popup already disables one when the other is set (`IsScenePresenceDisabled` / `IsTimeShiftDisabled`) — the UI knows they're one axis.

#### C6 — `quick-finish` is a dead marker
`[ClimaxMode:quick-finish]` is parsed, has a mutual-exclusion validator (`EnsureClimaxModeMutualExclusion`), and an `IsQuickFinishClimaxMode()` used only in deprecated `BuildFramingGuards()`. No injector, no engine path, no UI button. It only exists to throw if paired with `multi-encounter`.

#### C7 — Word Count: SteeringProfile range is inert (UI side)
Word count is already wired and lands at the very end of the prompt via `WritingStyleSlot` (Slot 18, Zone C): `Word Target: Target 200-400 words.` (Character) or `Target 400-800 words.` (Narrative, derived as `min*2` / `min(max*2, 1500)`). That part works and is in the right recency position. The only issue is on the **config side**: `ResolveWritingStyleAsync` loads the SteeringProfile `WordTargetMin/Max`, validates them (fail-fast), then **immediately overwrites** them with the `[targetwords:*]` range (default "small" = 200–400):
```csharp
var resolvedMarker = wordTargetMarker ?? "small";
if (WordTargetMarkerRanges.TryGetValue(resolvedMarker, out var markerRange))
{
    wordTargetMin = markerRange.Min;   // overwrites the profile values just loaded
    wordTargetMax = markerRange.Max;
}
```
So the profile range is only used for FR-006 validation, never the prompt. The popup override works because `ApplyWordCount` runs after this. Not a prompt bug — a config-source ambiguity worth resolving as part of the redesign (decide one home for the default range).

#### C8 — Narrative variant gets ZERO scene-direction HCs
`FinalInstructionSlot` gates every pacing/beat/timeshift/granularity/scene-presence HC on `!isNarrative`. The Narrative actor (which synthesizes the whole turn) gets only the "Action" block (zero dialogue + physical checklist). The user's pacing choice has no path to influence the narrative summary.

#### C9 — Two-layer time control doesn't coordinate
1. **Prompt-side:** `TimeShift` HC in `FinalInstructionSlot`.
2. **Engine-side:** multi-encounter/aftermath state machine (`CloseScene → AftermathCoupleInteraction → AdvanceTime`) that injects a System Instruction "Advance time to a new moment…".

They do not coordinate. `TimeShift = None` + multi-encounter theme → "no time skip" HC *and* "advance time" Instruction in the same context window.

#### C10 — UI mutual-exclusion checks are UI-only
`ContinuationSettingsPopup.razor` disables contradictory combos (e.g. Pacing=Slow disables BeatStyle=Single). These live only in `Is*Disabled()` helpers. The resolver and slots enforce nothing — a theme can declare `[Pacing:fast] [BeatStyle:single]` and produce "advance through multiple beats" *and* "resolve this moment in one turn".

### 2.3 Root cause: why it's "always pacing"

Pacing is the control users reach for first, and it is structurally muted by three independent mechanisms (C1: position 2+ hardcoded "Medium"; Fast suppressed on non-final beat turns; Narrative gets nothing). On top of that, the **default Climax config contradicts itself** (C3). So even when pacing fires, it fights two other HCs in the same prompt.

The other settings mostly work when they're the only thing touched, but combining them (the whole point of the popup) produces contradictory prompts due to defaults + missing resolver-side mutual exclusion.

### 2.4 Prompt-text effectiveness audit (value by value)

> **Added 2026-08-18 after review.** The audit above covers *structure* (which fields are wired, which are dead). This section covers the *wording* — whether the actual text the model receives produces the behavior each label promises. **This is the piece that a re-bundling alone would NOT fix.** The consolidation to Tempo + Span only works if the prompt text is rewritten, not re-arranged.

#### Pacing wording

| Value | Slot 17 HC text (position 1, final beat turn) | Assessment |
|---|---|---|
| **Slow** | `HARD CONSTRAINT — Scene Pacing: Slow pacing — advance within the current beat. Do not leap to a new beat or position.` | 🔴 **Weak/oxymoronic.** "Advance within the current beat" contradicts itself — the model reads "advance" first and treats it as an escalation license. No instruction to add sensory/emotional detail. |
| **Medium** | `HARD CONSTRAINT — Scene Pacing: Medium pacing — advance the scene by one beat, then stop. Do not skip ahead in time or jump to a new location.` | ✅ **Mostly works.** "Then stop" + two concrete negatives. Best-worded of the three. |
| **Fast** | `HARD CONSTRAINT — Scene Pacing: Fast pacing — advance through multiple beats. Push the story forward rapidly.` | 🔴 **No ceiling.** Open license to compress as much as it wants — including full start→orgasm in one turn. This is why Fast overruns. |
| Position 2+ (any) | `HARD CONSTRAINT — Scene Pacing: Medium pacing — You are a subsequent actor — build on the beat already established this turn. Do not restart or jump past it.` | 🔴 **Hardcoded "Medium" regardless of `sceneDir.Pacing`** (C1). The chosen value never reaches positions 2/3. |

#### Beat Style / Span wording

| Value | Slot 17 injected text (position 1) | Assessment |
|---|---|---|
| **Single** (budget 1) | `This moment lasts a single turn — resolve it now.` | ✅ Works. |
| **Short** (budget 3) | `This moment spans 3 turns. You are on turn N of 3 — establish it only / develop it further. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.` (turns 1–2); `…bring it to its climax or conclusion now and move on.` (turn 3) | ✅ **Strong wording.** Explicit turn position + hard negative on resolution + "end mid-action". Best-worded control in the system. |
| **Extended** (budget 5) | Same shape as Short, budget 5. | ✅ Generic cursor. 🔴 Episodic Climax cursor ignores the override (C2). |

**Key structural fact:** the default `BeatScope=Short` (budget 3) means Beat Style *suppresses* both Pacing (Fast only fires on turn 3) and Granularity (always force-overridden). Beat Style works; it silently neuters the other two controls.

#### Time Shift wording

| Value | Slot 17 injected text | Assessment |
|---|---|---|
| **None** | `HARD CONSTRAINT — Time Shift: None — No time skip — continue from the exact moment.` | ✅ Works. |
| **Small** | `HARD CONSTRAINT — Time Shift: Small — Minutes to a few hours.` | ⚠️ **No verb.** Noun phrase, not an instruction. Model often treats it as descriptive context, not a directive. |
| **Medium** | `HARD CONSTRAINT — Time Shift: Medium — Hours to half a day.` | ⚠️ Same (no verb). **Plus this is the Climax default** (C3) — contradicts the "stay in the moment" Pacing HC in the same prompt. |
| **Large** | `HARD CONSTRAINT — Time Shift: Large — A day or more.` | ⚠️ Same (no verb). |

#### Granularity wording

| Value | When `beatBudget <= 1` | When `beatBudget > 1` (the default under Short) |
|---|---|---|
| **Micro** | `…Micro — One response = one moment.` | `…Micro — One response covers one step of this multi-turn moment. Do not compress…` |
| **Meso** | `…Meso — One response = one scene/beat.` | `…Meso — One response covers one step… Do not compress…` |
| **Macro** | `…Macro — One response = a day or significant span.` | `…Macro — One response covers one step… Do not compress…` |
| **Montage** | `…Montage — One response = multiple days to weeks.` | `…Montage — One response covers one step… Do not compress…` |

🔴 **Wording is fine when Beat Style = Single, but dead under default config.** When `beatBudget > 1` the description is force-replaced with the same "one step of a multi-turn moment" string for ALL four values (C4). Micro/Meso/Macro/Montage produce identical prompt text.

#### Working controls (no changes needed)

- **Deepening** (Slot 3, position 2+): `- You are a subsequent actor this turn. Deepen the moment established by the first response from your character's perspective. Do not advance to a new beat or position.` — ✅ clear verb, clear negative, scoped. Keep as-is.
- **Word Count** (Slot 18, end of prompt): `Word Target: Target 200-400 words.` — ✅ concrete, numeric, right position.

#### Three defect classes (the real fix list)

1. **Broken wording** — Slow (oxymoron), Fast (no ceiling), TimeShift Small/Medium/Large (no verb). These behave badly regardless of whether they're emitted from a "Tempo" bundle or standalone.
2. **Structural suppression** — Beat Style (default `Short` budget) silently neuters Pacing and Granularity. A Tempo bundle still containing `Pacing=Fast + BeatScope=Short` will still only fire Fast on turn 3 of 3.
3. **Hardcoded bypass** — Position 2+ ignores the Pacing value (hardcoded "Medium"). A Tempo bundle setting `Pacing=Push` still emits "Medium" to position 2+.

**Conclusion: the redesign requires THREE things, not one.** (a) The Tempo+Span bundle (UX surface), (b) rewritten verb-driven prompt text per Tempo value, and (c) rewritten Slot 17 branch logic that emits ONE HC from the resolved Tempo to ALL positions. Any one without the other two fails.

---

## 3. Proposed Design

> **⚠️ Scope note (added 2026-08-18).** This is **not** a re-labeling exercise. Per §2.4, the current prompt text has three defect classes (broken wording, structural suppression, hardcoded bypass). The design below therefore specifies BOTH the user-facing bundle AND the rewritten prompt text + rewritten Slot 17 branch logic. The prompt text is the actual fix; the bundle is the UX surface.

### 3.1 Collapse to two user-facing controls

| New control | Maps to (internal) | Replaces |
|---|---|---|
| **Tempo** (Linger / Steady / Push / Leap) | `Pacing` + `TimeShift` + `Granularity` (coordinated) | Pacing, Time Shift, Granularity, Scene Presence |
| **Span** (Moment / Scene / Day / Montage) | `BeatScope` budget + `Granularity` default | Beat Style (user-facing), Granularity (user-facing) |

Each Tempo preset is a **coherent bundle** — contradictions (C3) become structurally impossible because the bundle is resolved in one place (`ContinuationOverrideResolver`):

| Tempo | Pacing | TimeShift | Granularity | Intended use |
|---|---|---|---|---|
| **Linger** | Slow | None | Micro | Intimate/pivotal moments, sensory depth |
| **Steady** | Medium | Small | Meso | Default scene-level advancement |
| **Push** | Fast | Medium | Meso | Compress beats, move toward resolution |
| **Leap** | Fast | Large | Macro/Montage | Aftermath, time-skip, "over the next weeks" |

Span (Moment/Scene/Day/Montage) sets the beat budget via `BeatScope` (Single=1 / Short=3 / Extended=5) and the default Granularity for the beat.

### 3.2 Engine behaviors stay user-toggleable, but coordinated

- **Multi-encounter** and **Aftermath** stay theme-declared **and remain primary popup rows** (not buried behind Advanced). They are story-structure choices the user wants visible control over.
- A separate **Theme Guidance UI** is planned so users do not have to know or type the raw markers (`[ClimaxMode:multi-encounter]`, `[Aftermath:husband-contrast]`, etc.) directly — the popup and the theme editor both expose the same controls. The marker system stays as the persistence format; the UI is the authoring surface.
- The engine time-skip machine should **read the resolved Tempo**: under Linger/Steady, the injected CloseScene/AdvanceTime instruction honors "stay present"; under Leap it aligns with "advance time". This fixes C9 by making the engine consume the same resolved value the prompt consumes.

### 3.3 Fix the structural pacing gaps (C1, C8)

- Position 2+ gets a **Tempo-aware** containment line, not the hardcoded "Medium": Linger → "deepen the beat, do not advance"; Push → "advance the beat, do not restart".
- Narrative variant is left alone on the pacing axis. Narrative is a **synthesis of the other characters' turns**, not a paced actor — it should not receive a tempo directive. The existing `FinalInstructionSlot` Action block (zero dialogue + physical detail checklist) stays as the only Narrative directive. The C8 finding ("Narrative gets zero pacing HCs") is therefore **expected behavior**, not a defect — no fix needed.

### 3.4 Delete the dead surface (C5, C6)

- Remove `quick-finish` (marker, helper, mutual-exclusion check).
- Fold Scene Presence into Tempo=Linger (same semantics).
- Decide one home for word count: **recommendation** — keep the `[targetwords:*]` marker, drop the profile range from the prompt path (still validated for FR-006), show the *effective* range in the popup (already done — just stop calling it "profile range"). Fixes C7.

### 3.5 Enforce mutual exclusion in the resolver (C10)

Move the `Is*Disabled` rules from the razor into `ContinuationOverrideResolver.ApplySceneDirection` as explicit validations that **throw fail-fast** (per the repo's no-fallback contract). A theme declaring `[Pacing:fast] [BeatStyle:single]` should fail validation at theme-save time, not silently produce a contradictory prompt.

### 3.6 Single source of truth for "time movement"

Today "how much does time advance" is encoded in: `Pacing`, `TimeShift`, `Granularity`, `BeatScope` (cursor), the engine time-skip machine, and `[ScenePresence]` — six places. The design collapses them to **one resolved Tempo + one resolved Span**, consumed by both the prompt and the engine.

### 3.7 Finalized prompt text — Tempo & Span directives

> **Finalized 2026-08-19 design session.** Replaces the current Pacing + TimeShift + Granularity trio with **two** HCs per turn: one Tempo (density) + one Span (duration). Every value has an imperative verb, an explicit ceiling, an explicit floor where relevant, and no internal contradiction. Position 2+ gets a Tempo-aware variant (fixes C1). Narrative gets no tempo/span HC (§3.3).

#### Design principles applied

1. **Imperative verb first** — stay / advance / compress / leap. No noun-phrase "descriptions."
2. **Explicit ceiling** on every value — the model can't overrun (fixes Fast's no-ceiling bug).
3. **Explicit floor** where relevant — don't stall, don't loop.
4. **Concrete units** — turns, beats, hours, days. No abstract "momentum" or "rapidly."
5. **What to do AND what not to do** — positive instruction + negative guard in every HC.
6. **One density axis (Tempo), one duration axis (Span)** — they change at different rates (Tempo per-beat, Span per-turn) and control different things, so they stay as two adjacent HCs rather than one combined block.

#### Tempo directives (position 1, lead actor)

**Tempo = Linger** — intimate/pivotal moments, sensory depth (was Slow + TimeShift=None + Granularity=Micro)
```
HARD CONSTRAINT — Tempo: Linger. Stay in this exact moment — do not advance time, do not leap to a new beat, position, or location. Deepen what is happening right now: sensory detail, internal reaction, the texture of this moment. One response covers one moment, not a scene.
```

**Tempo = Steady** — default scene-level advancement (was Medium + TimeShift=Small + Granularity=Meso)
```
HARD CONSTRAINT — Tempo: Steady. Advance the scene by one beat, then stop. You may shift time forward by minutes to a few hours — no more than half a day. One response covers one scene or beat. Do not skip ahead by a day or more, and do not leap to a new location without a transition.
```

**Tempo = Push** — compress toward resolution, Climax acceleration (was Fast + TimeShift=Medium + Granularity=Meso)
```
HARD CONSTRAINT — Tempo: Push. Advance through two to three beats this response — compress toward the natural resolution of this moment. You may shift time by hours to half a day. Do not compress an entire arc (start to climax) into one response unless this is the final beat of the arc. One response covers one scene or beat of compressed action.
```

**Tempo = Leap** — aftermath, time-skip, montage (was Fast + TimeShift=Large + Granularity=Macro/Montage)
```
HARD CONSTRAINT — Tempo: Leap. Advance time by a day or more — skip routine time and land on the next meaningful moment. Summarize what passed in a sentence or two; focus the response on the new day, the new circumstance. One response covers a day, a significant span, or multiple days to weeks. Do not stay in the previous moment.
```

#### Tempo directives (position 2+, subsequent actors — fixes C1)

Position 2+ builds on the beat established by position 1. The Tempo tells them HOW to build on it. **No more hardcoded "Medium."**

```
HARD CONSTRAINT — Tempo: {Linger|Steady|Push|Leap}. You are a subsequent actor this turn — build on the beat already established by the first actor. {Tempo-specific instruction}
```

Tempo-specific instruction for position 2+:
- **Linger**: `Deepen the moment from your character's perspective — sensory detail, internal reaction. Do not advance the beat or introduce a new position.`
- **Steady**: `Advance the beat one step from your character's perspective. Do not restart the scene or jump past the established beat.`
- **Push**: `Carry the beat toward its resolution from your character's perspective. Compress forward — do not restart.`
- **Leap**: `You are now in the new moment the first actor established. Begin the new scene from your character's perspective — do not return to the previous beat.`

#### Span directives (per turn position — replaces Beat Style stage)

Span controls how many turns the current moment lasts. The wording preserves the existing Beat Style strength (explicit turn position + hard negative on resolution + "end mid-action"), just relabeled.

**Span = Moment** (budget 1, was BeatStyle=Single)
```
HARD CONSTRAINT — Span: Moment. This moment lasts a single turn — resolve it now.
```

**Span = Scene** (budget 3, was BeatStyle=Short) — turn-position-aware:
- Turn 1 of 3: `HARD CONSTRAINT — Span: Scene. This moment spans 3 turns. You are on turn 1 of 3 — establish it only. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.`
- Turn 2 of 3: `HARD CONSTRAINT — Span: Scene. This moment spans 3 turns. You are on turn 2 of 3 — develop it further. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.`
- Turn 3 of 3 (final): `HARD CONSTRAINT — Span: Scene. This moment spans 3 turns. You are on turn 3 of 3 — bring it to its climax or conclusion now and move on.`

**Span = Extended Arc** (budget 5, was BeatStyle=Extended) — same shape, budget 5:
- Turn P of 5 (P < 5): `HARD CONSTRAINT — Span: Extended Arc. This moment spans 5 turns. You are on turn {P} of 5 — {establish it only | develop it further}. Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action, before the resolution.`
- Turn 5 of 5 (final): `HARD CONSTRAINT — Span: Extended Arc. This moment spans 5 turns. You are on turn 5 of 5 — bring it to its climax or conclusion now and move on.`

Span is a **lead-actor directive** (position 1 only). Subsequent actors already get the Tempo-aware "build on the beat" constraint, so they must not also receive a conflicting duration directive (same as the current `isLeadActor` gate in `FinalInstructionSlot`).

#### Tempo × Span reconciliation

Tempo (density) and Span (duration) control different axes and change at different rates — Tempo is per-beat, Span is per-turn. They can appear to conflict when Tempo=Push says "compress toward resolution" but Span says "do NOT conclude this turn" on a non-final turn. The reconciliation is explicit:

- **Span wins on WHEN to conclude.** On non-final turns, Span's "Do NOT bring the moment to its climax or conclusion this turn. End your response mid-action" overrides Tempo=Push's "toward resolution" — the model compresses WITHIN the moment but does not CONCLUDE it.
- **Tempo wins on HOW MUCH to compress.** On every turn, Tempo controls how many beats the response covers (Linger: 0 beats, deepen; Steady: 1 beat; Push: 2–3 beats; Leap: a day+).
- **On the final turn, they align.** Span says "bring it to conclusion now" and Tempo=Push says "toward resolution" — no conflict.

This interaction must be covered by unit tests: e.g., `Tempo_Push_Span_Scene_Turn1_EmitsBothHCs_SpanWinsOnConclusion`, `Tempo_Push_Span_Scene_Turn3_EmitsBothHCs_Aligned`.

#### Narrative variant

NO tempo/span HC — Narrative is a synthesis of the other characters' turns, not a paced actor (§3.3). The existing `FinalInstructionSlot` Action block (zero dialogue + physical detail checklist) stays as the only Narrative directive.

#### What changed vs today

1. **One density HC (Tempo) + one duration HC (Span)** replace the current three competing HCs (Pacing + TimeShift + Granularity) — C3 becomes structurally impossible.
2. **Every value has an imperative verb** — fixes Slow's oxymoron ("advance within") and TimeShift's noun phrases ("Minutes to a few hours.").
3. **Every value has an explicit ceiling** — fixes Fast's no-ceiling overrun (Push: "2–3 beats, do not compress an entire arc unless final beat"; Linger: "do not advance time"; Steady: "no more than half a day"; Leap: "a day or more").
4. **Position 2+ gets a Tempo-aware variant** — fixes C1's hardcoded "Medium." The chosen Tempo actually reaches positions 2/3.
5. **Span wording preserved from Beat Style** — the strongest existing wording (turn position + hard negative + "end mid-action") is kept, just relabeled.
6. **Tempo × Span reconciliation is explicit and test-covered** — no more silent suppression (C4's force-override of Granularity is gone because Granularity is no longer a separate HC).

---

## 4. Plan — Implementation Tasks (persisted 2026-08-19)

> **Order:** the prompt-text rewrite (§3.7) comes before the UI, because the current wording is broken and would be carried into the new UI unchanged. Tasks T1–T3 are the actual behavioral fix; T4–T8 are the UX/cleanup layer. Each task is independently shippable and testable.

### T1 — Fix the Climax default contradiction (C3)
- **Files:** `DreamGenClone.Web/Application/RolePlay/SceneDirectionResolver.cs` — `PhaseDefaultTimeShiftMap[Climax]` Medium→None; `[Reset]`→Large.
- **Tests:** `DreamGenClone.Tests/RolePlay/SceneDirectionResolverTests.cs` — assert Climax resolves `TimeShift=None`, Reset resolves `TimeShift=Large` with no theme.
- **Acceptance:** resolving `"Climax"` with no theme yields `TimeShift=None`; default Climax prompt no longer emits a contradicting "Hours to half a day" Time Shift HC.

### T2 — Fix position-2+ hardcoded "Medium" (C1)
- **Files:** `DreamGenClone.Web/Application/RolePlay/Prompts/Slots/FinalInstructionSlot.cs` — position-2+ containment line reads the resolved Tempo (or, pre-Tempo, `sceneDir.Pacing`) instead of the hardcoded `"Medium"`.
- **Tests:** `FinalInstructionSlotTests`, `SceneDirectionConsolidationTests` — assert position-2+ text reflects the selected Tempo/Pacing.
- **Acceptance:** the chosen Tempo/Pacing reaches positions 2/3; no `"Medium"` hardcoded when Tempo≠Steady.

### T3 — Rewrite the prompt text per §3.7 (core fix)
- **Files:** `FinalInstructionSlot.cs` — emit ONE Tempo HC (with ceiling) + ONE Span HC (turn-position-aware) + Tempo-aware position-2+ variant, replacing the current Pacing + TimeShift + Granularity + ScenePresence HCs. `ContinuationMarkerCatalog.cs` — labels/descriptions updated to Tempo/Span (Linger/Steady/Push/Leap, Moment/Scene/Extended Arc). Remove `DescribeTimeShift`/`DescribeGranularity`/`DescribeScenePresence` call sites as they become unused.
- **Tests:** update `SceneDirectionConsolidationTests` + `FinalInstructionSlotTests`; add `TempoSpanDirectiveTests` covering: each Tempo value emits the §3.7 verbatim HC; each Span turn-position variant (1..N and final) emits correct text; **Tempo×Span reconciliation** (`Tempo_Push_Span_Scene_Turn1_EmitsBothHCs_SpanWinsOnConclusion`, `Tempo_Push_Span_Scene_Turn3_EmitsBothHCs_Aligned`).
- **Acceptance:** every Tempo value has imperative verb + explicit ceiling; no internal contradiction; C4 (Granularity force-override) gone because Granularity is no longer a separate HC.

### T4 — Add a Tempo/Span bundle type
- **Files:** new `SceneTempo`/`SceneSpan` enums (or reuse `ScenePacing`/`BeatScope` mapped to Tempo/Span); `SceneDirectionResolver` resolves Tempo+Span from theme markers/phase defaults; `ContinuationOverrideResolver` maps the user override (Tempo → Pacing+TimeShift+Granularity coherently; Span → BeatScope budget). Keep the raw fields for power-user Advanced disclosure + theme-marker compatibility.
- **Tests:** `ContinuationOverrideResolverTests`, `SceneDirectionResolverTests` — bundle mapping is coherent (no contradictory field combos produced).
- **Acceptance:** a Tempo bundle can never resolve to contradictory field values (e.g. Linger with TimeShift=Large).

### T5 — Wire the engine time-skip machine to read resolved Tempo (C9)
- **Files:** `DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs` (ContinueAsAsync time-skip directive text) — CloseScene/AdvanceTime instructions honor Linger/Steady vs Leap.
- **Tests:** `MultiEncounterTimeSkipTests`, `AftermathHusbandContrastTests`.
- **Acceptance:** `TimeShift=None` (Linger) + multi-encounter no longer produces a contradictory "advance time" Instruction in the same context window.

### T6 — Replace the popup's 9 dials with Tempo + Span
- **Files:** `DreamGenClone.Web/Components/Pages/ContinuationSettingsPopup.razor`, `RolePlayWorkspace.razor` — Tempo + Span as primary rows; Multi-encounter and Aftermath remain their own rows; raw fields (Pacing/TimeShift/Granularity/BeatScope) behind an "Advanced" disclosure. Parallel **Theme Guidance UI** surfaces the same controls in the theme editor so markers don't have to be hand-typed.
- **Tests:** popup gating helpers (`Is*Disabled`) updated to Tempo/Span semantics; no UI-only mutual-exclusion gaps.
- **Acceptance:** a user can control flow/pacing/rhythm with Tempo + Span only, without knowing markers or which combos work.

### T7 — Delete `quick-finish` and Scene Presence (C5, C6)
- **Files:** `RolePlayAssistantPrompts.cs` (remove `IsQuickFinishClimaxMode` + `EnsureClimaxModeMutualExclusion` if multi/quick pair no longer possible), `SceneDirectionResolver.cs` (drop `ResolveScenePresence`), `ContinuationMarkerCatalog.cs` (drop `DescribeScenePresence`), `FinalInstructionSlot.cs` (drop Scene Presence HC).
- **Tests:** update `SceneDirectionResolverTests` + any quick-finish/presence tests to the removed surface.
- **Acceptance:** no dead marker/helper/validator remains; Tempo=Linger covers the stay-present semantic.

### T8 — Move mutual-exclusion checks into the resolver (C10)
- **Files:** `ContinuationOverrideResolver.cs` — explicit fail-fast validation on contradictory override/theme combos; theme-save-time validation in the theme editor/service.
- **Tests:** fail-fast tests — a theme declaring `[Tempo:Push] [Span:Moment]`-style contradictions throws with an explicit diagnostic.
- **Acceptance:** contradictory combos fail fast with a diagnostic (per the repo's no-fallback contract); never silently produce a contradictory prompt.

Each step is independently shippable and testable. T1–T3 are the behavioral fix (validated against real built prompts per the pacing-directive findings checklist); T4–T8 are the UX/cleanup layer.

---

## 5. Related Work

- **B-082** — Continuation settings popup (the 9-setting surface this redesign replaces).
- **B-084** — Wire theme-driven Granularity into the prompt (superseded by Tempo bundles).
- **B-085** — Beat Style override into Climax Beat Cursor (superseded by Span; the episodic-cursor gap must be resolved either way).
- **B-052** — Prompt-injection contradiction cleanup.
- **B-068** — Pacing prompt injection (prior pacing work; preserved under Tempo).
- **B-070** — Scene transition / advance time (interacts with Tempo=Leap).

## 6. Open Questions

1. Should Tempo/Span replace the marker system entirely, or remain a per-session override layered on theme markers? (Recommendation: keep theme markers as the theme author's default Tempo/Span; the popup overrides for the session.)
2. Where should Span's Granularity default live — fixed per Span value, or theme-authorable?
3. Should word-count stay in the popup or move to the Style Profile only (removing `[targetwords:*]`)? (Recommendation: keep the marker, it's the theme author's per-phase control.)
4. **Validation of the draft directives (§3.7)** — the new Tempo text must be verified against real built prompts (canonical check: `Sessions.PayloadJson.interactions[].promptText` / `PromptBuilt` debug events), per the pacing-directive findings checklist. Do we want a manual session test matrix per Tempo value (Linger/Steady/Push/Leap) before wiring the UI?
5. **Beat Style default suppression** — since `BeatScope=Short` (budget 3) neuters Pacing/Granularity, should the Tempo/Span UI expose an explicit \"beat holds\" toggle, or make Span the only way to change beat duration (removing the silent interaction)?
