Searched codebase for "BuildOpeningNarrativePromptAsync scenario opening narrative prompt spouse couple", 21 results

Good design question. Let me map the full system first so I can give precise, data-driven suggestions 

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs#1928-1928), lines 1928 to 1970

Searched for text `BuildOpeningNarrativePromptAsync` (`**/*.cs`), 2 results

Searched for text `class CharacterProfile` (`**/*.cs`), 2 results

Searched for text `class HusbandAwarenessProfile` (`**/*.cs`), 2 results

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs#1972-1972), lines 1972 to 1990

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Domain/StoryAnalysis/CharacterProfile.cs#1-1), lines 1 to 50

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Domain/StoryAnalysis/HusbandAwarenessProfile.cs)

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Application/RolePlay/RolePlayEngineService.cs#2385-2385), lines 2385 to 2430

Searched for regex `class (Scenario|Character|Opening|Plot)` (`**/DreamGenClone.Web/Domain/Scenarios/*.cs`), 5 results

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Domain/Scenarios/Scenario.cs#1-1), lines 1 to 120

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Domain/Scenarios/Plot.cs)

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Domain/Scenarios/Character.cs)

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs#480-480), lines 480 to 640

Searched for regex `BuildHusbandAwarenessFrame|HusbandAwarenessProfileId|CharacterProfile|earlyAwarenessFrame|CharacterSnapshots` (`**/DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs`), no results

Searched for regex `BuildFramingGuards` (`**/*.cs`), 20 results

Searched for text `AppendScenarioGuidance` (`**/*.cs`), 9 results

Read [](file:///d%3A/src/DreamGenClone/DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs#1-1), lines 1 to 300

Good question — this is a design/strategy question about what data shapes narrative focus. Let me map the full system first.

The LLM generates narratives from seven prompt inputs — each is a lever you can adjust:

```
┌─────────────────────────────────────────────────────────────────┐
│                    WHAT SHAPES NARRATIVE FOCUS                    │
├────────────┬────────────────────────────────────────────────────┤
│ Opening    │ BuildOpeningNarrativePromptAsync                   │
│ Narrative  │ → scenario characters, RelationTargetId, locations │
├────────────┼────────────────────────────────────────────────────┤
│ Character  │ Character list in every prompt                     │
│ List       │ → "Dean [OtherMan] [Affair partner]: desc..."      │
├────────────┼────────────────────────────────────────────────────┤
│ Behavioral │ HARD CONSTRAINT per character                      │
│ Frames     │ → CharacterProfile.EncounterStats → dimension tiers│
│            │ → HusbandAwarenessProfile.Notes (deprecated)       │
├────────────┼────────────────────────────────────────────────────┤
│ Scenario   │ Plot.Goals, Plot.Conflicts, NarrativeGuidelines    │
│ Context    │ → injected as "- Plot Goals:" into every prompt     │
├────────────┼────────────────────────────────────────────────────┤
│ Theme      │ Active theme (infidelity-public-facade-v3)         │
│ Guidance   │ → PhaseGuidance, AIGuidanceNotes, HardConstraints  │
├────────────┼────────────────────────────────────────────────────┤
│ Phase      │ BuildFramingGuards("BuildUp")                      │
│ Guards     │ → "tension and anticipation only, no explicit acts"│
├────────────┼────────────────────────────────────────────────────┤
│ Stat       │ "Becky current state: Desire=69, Restraint=37..."  │
│ States     │ "Dean current state: Desire=80, Restraint=47..."   │
└────────────┴────────────────────────────────────────────────────┘
```

For session 6d972d72, here's what drives Dean (OtherMan) references even when he's excluded from acting:

| Input | What it says | Why Dean gets referenced |
|---|---|---|
| **Theme** | `infidelity-public-facade-v3` active | The word "infidelity" primes the LLM for an affair partner |
| **Character list** | `Dean [Role: OtherMan] [Relation: Becky's affair partner]: ...` | Explicitly names and describes him in every prompt |
| **Husband frame** | "He is completely unaware... the partner has no idea anything is happening" | Frames husband as oblivious — the implicit counterpoint is "what he's unaware OF" = the OtherMan |
| **Stat states** | Dean Desire=80, Becky Desire=69 | High Desire on the affair pair reinforces the dynamic |
| **Plot goals** | Likely mention "secret affair" or "hidden relationship" | The goal itself names the OtherMan's role |

---

### What to change — organized by your concepts

#### 1. Scenario-level (scenario JSON / UI)

**Plot.Goals** — rewrite early goals to focus on the couple:
```json
// BEFORE (guessing):
"Goals": ["Wife begins a secret affair with Dean behind her husband's back"]

// AFTER:
"Goals": [
  "Establish the husband and wife's domestic routine and relationship dynamic",
  "The natural drift begins — small moments of distraction, unmet attention",
  "Other characters emerge into the wife's awareness gradually, not as immediate threats"
]
```

**Plot.Description** — frame the scenario around the RELATIONSHIP, not the affair:
```
"A married couple at a campground. The wife feels a growing restlessness 
in her marriage — her husband is loving but inattentive. Over time, the 
proximity of other campers opens possibilities she hadn't considered."
```

**Narrative.NarrativeGuidelines** — add early-focus directives:
```json
"NarrativeGuidelines": [
  "First 4-6 interactions: establish the husband-wife dynamic, their interaction patterns, and the setting. Other characters are present in the scene but are not the focus.",
  "Do not reference or imply the OtherMan's presence in any way until after the couple dynamic is firmly established.",
  "The husband is the wife's immediate world — write him as present, active, and engaged in his own way."
]
```

#### 2. Scenario character descriptions

The Wife's description in the scenario character list is injected into EVERY prompt:

```
// BEFORE:
Becky [Role: Wife]: A restless wife drawn to the attention of another man...

// AFTER:
Becky [Role: Wife]: A wife navigating the familiar rhythms of her marriage. 
She loves her husband but feels something missing — a quiet hunger she hasn't 
named yet. She is drawn to attention but doesn't seek it out.
```

The OtherMan's description should de-emphasize early presence:
```
Dean [Role: OtherMan]: A fellow camper staying nearby. Charismatic but 
unassuming. His presence is initially peripheral — he becomes relevant 
only as circumstances bring characters closer over time.
```

#### 3. CharacterProfile (B-042) — This is the key lever for husband behavior

The `CharacterProfile` with `TargetRole: "Husband"` controls the behavioral frame injected as a HARD CONSTRAINT. The `EncounterStats` dictionary determines the dimension tiers, and `AdditionalNotes` adds override text.

**Oblivious Husband profile:**
```json
{
  "Name": "Oblivious Husband",
  "TargetRole": "Husband",
  "EncounterStats": {
    "Awareness": 10,
    "Acceptance": 5,
    "Voyeurism": 0,
    "Participation": 0,
    "Encouragement": 0,
    "RiskTolerance": 5
  },
  "AdditionalNotes": "He is deeply absorbed in his own world — his hobbies, his reading, his routines. He interacts with his wife casually and affectionately but without intensity. His attention is on his own activities, not on what she is doing. He is not suspicious. He is not watching. He is simply elsewhere in his own mind."
}
```

This produces a HARD CONSTRAINT like:
> HARD CONSTRAINT — Ken behavioral frame: He is deeply absorbed in his own world... His attention is on his own activities, not on what she is doing...

This makes the husband THE SUBJECT of his own narrative rather than "the one who doesn't know."

**Proud Hotwife profile:**
```json
{
  "Name": "Proud Hotwife",
  "TargetRole": "Husband",
  "EncounterStats": {
    "Awareness": 80,
    "Acceptance": 90,
    "Voyeurism": 70,
    "Participation": 20,
    "Encouragement": 85,
    "RiskTolerance": 60
  },
  "AdditionalNotes": "He actively wants his wife to attract male attention. He derives pride and arousal from watching other men desire her. In early interactions, he creates opportunities for her to be noticed — choosing her outfits, positioning her where others can see, complimenting her in ways that draw eyes. He orchestrates. He is not passive."
}
```

The `FullOverride: true` flag would make `AdditionalNotes` the ENTIRE behavioral frame instead of appending to generated tier text.

#### 4. Theme-level guidance

The active theme's `PhaseGuidance` entries for `BuildUp` phase can be adjusted:
```json
"PhaseGuidance": [
  {
    "Phase": "BuildUp",
    "GuidanceText": "Early BuildUp: Establish the domestic couple scene. The husband and wife are the primary characters. Other characters are in the environment but are background — do not bring them into focus until the couple dynamic is set."
  }
]
```

Or via `AIGuidanceNotes` with `Section: "HardConstraint"`:
```
"Other characters (including the OtherMan) must remain peripheral background 
presence during early interactions. They are in the scene but are NOT the focus 
of any character's attention, thoughts, or dialogue."
```

#### 5. Opening narrative — can be made stronger

The current `coupleClause` is:
```
"The scene opens with Ken and Becky together — Ken is the persona's character, 
and Becky is their partner. Ground the opening in their interaction with each other."
```

This can be more directive (edit `BuildOpeningNarrativePromptAsync`):
```
"The scene opens with Ken and Becky together — they are a married couple. 
Write ONLY about their interaction. Describe what Ken is doing, what Becky 
observes about him, and their immediate environment. Other characters may be 
present in the campground but do not mention them by name or bring them into 
the scene. This opening is exclusively about the couple."
```

---

### Summary: what to change for immediate effect

| Priority | Where | What | Effect |
|---|---|---|---|
| **1** | CharacterProfile (Husband) | Rewrite `AdditionalNotes` to focus on HIS world/actions | Husband becomes the subject, not the obstacle |
| **2** | Scenario Plot.Goals | First goal = "establish couple dynamic" | LLM's task framing changes |
| **3** | Scenario NarrativeGuidelines | Add "early interactions: couple only" directive | Explicit focus constraint |
| **4** | Scenario Character descriptions | De-emphasize OtherMan in descriptions | Reduces OtherMan priming |
| **5** | Theme PhaseGuidance | Add BuildUp couple-first guidance | Theme-level reinforcement |
| **6** | Opening narrative prompt | Make `coupleClause` more directive | Opening scene starts right |

The most impactful single change is #1: **rewrite the Husband CharacterProfile's `AdditionalNotes`** (or create a new one and assign it to the scenario). This changes the HARD CONSTRAINT text from "he is unaware" (which implicitly centers the affair) to "he is absorbed in his own world" (which centers HIM).