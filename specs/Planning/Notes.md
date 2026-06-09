The Steer command Apply Steer needs to ensure the continuation is at instruction and not on Message or Narrative by character, also the /steer can be removed from the text it has not affect.




When the rp workspace page is opened the left menu bar is always expanded, it should to the opposite and auto collapse

Done - Analyse the rp enging and the themeinfidelity-public-facade-v3
and  and its data and offer opinions on how to make the rp interactions skip time more often naturally, to end beats without having to be given directions, to occasionally fade to black (never fade to black during or over a sex scene though), to skip time so that not every beat is exactly after then next one in time, also for all themes or the rp engine how to ensure the first few or starting interactions focus on the husband and wife role, how there character profiles interact with each other to setup the initial scenario and scene, currently the other man is almost always in the beginning, the husband role and wife role do not interact much, generally want more interactions between husband and wife when appropriate without breaking the engine flow.



The steer prompts provided when the husband is close by and in line of sight are not believable, they need to have more defined limitations with the physical world and line of sight, not sure what specifically needs to change


multiple otherman role should be in competition for Beckys affection, the otherman role should never take the ovberveer or follow with the husband role, the otherman is 


All themes or rp engine 
There needs to be more natural progression of time and time skips, it will do it some time, but most iteractions are always immediately after each other in time, it needs to be smart about it and not skip time in the middle of an encounter, but currently the wife will reject the other man, next interaction he tries again, there should be a time jump or a character focus move, or something, the rp engine needs to take into account the Scenarios Time Frame and try to adhere  to it and adapt to it. If the time frame stats several hours vs over several months there should be an obvious differenct in the time shifts that can happen.  If no time frame is in the scenario a long running time frame is assumed, this means the scenario is a real life day to day type time frame which could span days, months, or years in the narrative.



infidelity-brief-disappearance

in any phase after an encounter and the return to husband, there needs to be more passage of time, more interactions between husband and wife and other background characters, currently it reads like they finish an act, then 2 minutes later go again

the theme is meant to emphasize the duality of the wife role, how she can go and sneak away with another man, then return to the husband like nothing has happened, the interactions and internal and external thougths are important,




Memory update - memory needs to shorter and more concise, only the sex interactions occurred with, location,  positions, where the male ejaculated need to be remembered.  The memory should be recalled on occasion to ensure repeat scenarios are kept to a minimum.



under the Behavioral Dimensions add the Behavioral Prompt Texts, the sections should be collapsible collapsed by default.

When a resistant wife thinks she should go back to her husband, when resistance is high enough she should, currently the wife thinks it but then does not.  The otherman role will need to chase her in his scenario.  I do no want this a hard coded prompt thing it should be in the data somewhere. 

There are no other characters or background people being mentioned even in passing, i believe this was because at one time when characters were mentioned it would start tracking stats for them and this caused issues, it was changed to only track stats for characters and persona in the scenario, but inadvertanly it now no longer every mentions the presence of other people, even when the scenario is a house party and there are definetiley other people around.

---

# Analysis: Persona Physical Attributes Not Driving Model Behavior in RP Prompts

**Date:** 2026-06-09
**Session:** `19b9abd8-08db-4ca7-8d12-307b61da25dc` ("The Party Reason Resistance")
**Interactions:** `1dae2985-f129-4459-9e7e-9056a463b704` and `5f768857-ba6c-44b9-98c0-9b3c3acbe6f`

## Summary

The Husband persona (Ken) has detailed Intimate — Male physical attributes populated (below-average endowment, quick stamina, low skill, shy confidence). These should cause the model to generate scenes where the Wife is underwhelmed/unsatisfied during intimacy. However, the model is generating romantic, intense encounters instead — suggesting the attribute data is either not reaching the prompt or not being sufficiently emphasized for the model to act on.

---

## Data Inventory

### Persona: Ken (Husband) — POV Character
```
Source: Sessions.PayloadJson → $.personaPhysicalAttributes
Gender: Male | Role: Husband | Age: 51

Intimate — Male:
  EndowmentLength: "Below average length"
  EndowmentGirth:  "Slender"
  → Combined: "a below-average length, slender cock — below average in size; modest sensation"
  Stamina:            "Quick — rarely lasts long"
  Recovery:           "Very slow — needs significant time"
  EjaculationIntensity: "Below average"

Intimate — Shared:
  SexualSkill:      "Below average — lacks technique"
  SexualDrive:      "Low — rarely initiates"
  SexualConfidence: "Shyly submissive"
  OralSkill:        "Below average"
  Scent:            "Neutral"
```

### Character: Becky (Wife) — Scenario Character
```
Source: Scenarios.PayloadJson → $.Characters[0].PhysicalAttributes
Gender: Female | Role: Wife | Age: 50 | Attractiveness: 7/10

Intimate — Female:
  VaginalTightness:  "Extremely tight"
  Sensitivity:       "Highly sensitive"
  Lubrication:       "Very wet — gets soaked quickly"
  OrgasmicCapacity:  "Multi-orgasmic and easily triggered"

Intimate — Shared:
  SexualSkill:      "Skilled — above average with good technique"
  SexualDrive:      "High — regularly eager"
  SexualConfidence: "Passively receptive"
  OralSkill:        "Skilled"
```

### Character: Dean (Other Man) — Scenario Character
```
Source: Scenarios.PayloadJson → $.Characters[1].PhysicalAttributes
Gender: Male | Role: Unknown | Age: 45 | Attractiveness: 10/10

Intimate — Male:
  EndowmentLength: "Long"
  EndowmentGirth:  "Thick"
  → Combined: "a long, thick cock — well above average in every dimension; would feel noticeably filling and deeply penetrating"
  Stamina:            "Tireless — can go for hours"
  Recovery:           "Near-instant — ready again almost immediately"
  EjaculationIntensity: "Massive — forceful and copious"

Intimate — Shared:
  SexualSkill:      "Virtuoso — instinctively reads every response"
  SexualDrive:      "Insatiable — desires constantly"
  SexualConfidence: "Confidently assertive"
  OralSkill:        "Exceptional — utterly skilled"
  Scent:            "Intoxicatingly musky"
```

---

## Code Path Analysis

### How Attributes Flow Into Prompts

```
Database (Sessions.PayloadJson / Scenarios.PayloadJson)
  → Deserialization (SessionService / ScenarioService)
  → RolePlaySession.PersonaPhysicalAttributes / Scenario.Characters[].PhysicalAttributes
  → PhysicalAttributesFormatter.FormatBlock()
  → Injected into prompt text
  → LLM
```

### Prompt Construction — `RolePlayContinuationService.BuildPromptAsync()` (line 418–750)

Two injection points:

1. **POV Persona** (lines 430–443):
```csharp
if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
{
    sb.AppendLine($"POV Persona ({session.PersonaName}):");
    sb.AppendLine(session.PersonaDescription.Trim());
    var personaAppearance = PhysicalAttributesFormatter.FormatBlock(
        session.PersonaPhysicalAttributes);  // ← SHOULD include Ken's intimate data
    if (!string.IsNullOrEmpty(personaAppearance))
        sb.AppendLine(personaAppearance);
}
```

2. **Scenario Characters** (lines 622–643):
```csharp
foreach (var character in scenario.Characters)
{
    sb.AppendLine($"  {character.Name}{roleText}{relationSuffix}: {description}");
    var charAppearance = PhysicalAttributesFormatter.FormatBlock(
        character.PhysicalAttributes);  // ← Becky and Dean's attributes
    if (!string.IsNullOrEmpty(charAppearance))
        sb.AppendLine($"    {charAppearance}");
}
```

### `PhysicalAttributesFormatter.FormatBlock()` — NO Gender Filtering

The formatter unconditionally includes ALL fields for every character:
- Intimate — Shared (Scent, SexualSkill, SexualDrive, SexualConfidence, OralSkill)
- Intimate — Male (Endowment, Stamina, Recovery, Ejaculation)
- Intimate — Female (VaginalTightness, Sensitivity, Lubrication, OrgasmicCapacity)

Only null/empty fields are skipped. Since Ken's female attributes are null and male attributes are populated, only the male intimate section renders for him. This is correct behavior.

---

## Findings

### Finding 1: `InteractionRetryService` Missing Persona Physical Attributes ⚠️ BUG

**File:** `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs` (lines 252–260)

The retry prompt builder includes `session.PersonaDescription` but does **NOT** call `PhysicalAttributesFormatter.FormatBlock(session.PersonaPhysicalAttributes)`. Compare:

| Service | Persona Description | Persona Physical Attrs | Character Attrs |
|---|---|---|---|
| `RolePlayContinuationService` | ✅ | ✅ | ✅ |
| `InteractionRetryService` | ✅ | ❌ **MISSING** | ✅ |

**Impact:** When a user retries an interaction, the husband's intimate male data is stripped from the prompt. The model generates without knowing Ken's sexual performance characteristics.

### Finding 2: Prompt Structure — Persona Attributes May Be Lost In Noise

The POV Persona section (with Ken's description + appearance) appears at the very top of the prompt, followed by:
- HARD CONSTRAINT (location)
- Persona Role + Relation
- Full Scenario (name, description, plot, setting, goals, conflicts, narrative guidelines, world rules, environmental details)
- Intensity Profile
- Writing Style Profile
- Characters list (Becky + Dean with their appearances)
- Locations, Objects
- Interaction history
- Memory/encounter summaries
- Location truth state

Ken's intimate male attributes ("below average length, slender cock — below average in size; modest sensation") appear once at the top, while the prompt is thousands of tokens long. The model may simply not weigh this information sufficiently against the narrative context.

### Finding 3: Contrast Effect — Dean's Attributes Dwarf Ken's

Dean's attributes describe a sexual virtuoso while Ken's describe poor performance. The model sees both in the same prompt. Without explicit instruction to use these attributes to shape outcomes, the model defaults to generating the most narratively satisfying encounter — which naturally favors the more vivid/positive description.

### Finding 4: No Explicit Instruction To Use Attributes For Scene Outcomes

The prompt injects attributes as factual appearance data but gives no instruction like:
- "Use character sexual attributes to determine satisfaction and performance during intimate scenes"
- "A character with low stamina should be portrayed as struggling to perform"
- "The wife's satisfaction should reflect the husband's actual performance, not her desire"

Without such guidance, the model treats attributes as descriptive flavor rather than behavioral constraints.

### Finding 5: Ken Is POV Persona, Not A Scenario Character

Ken only exists as the POV Persona. He is NOT listed under "Characters in this scene:" — only Becky and Dean appear there. The "Characters in this scene:" section has more visual weight in the prompt structure. Ken's attributes appear in a separate section that may receive less model attention.

---

## Plan For Review

### Phase 1: Verify Attributes Reach The Prompt

**Action:** Dump the full prompt text for one of the PromptBuilt events to confirm Ken's intimate attributes are actually present.

**Method:** Query `RolePlayDebugEvents` where EventKind = 'PromptBuilt' and SessionId matches, extract and decode the `MetadataJson.prompt` field.

**Success criteria:** The prompt contains text like "Endowment: a below-average length, slender cock — below average in size; modest sensation; Stamina: Quick — rarely lasts long".

### Phase 2: Fix InteractionRetryService Gap

**File:** `DreamGenClone.Web/Application/RolePlay/InteractionRetryService.cs`

**Change:** Add persona physical attributes formatting after the persona description, matching the `RolePlayContinuationService` pattern:

```csharp
if (!string.IsNullOrWhiteSpace(session.PersonaDescription))
{
    sb.AppendLine($"POV Persona ({session.PersonaName}):");
    sb.AppendLine(session.PersonaDescription.Trim());
    // ADD:
    var personaAppearance = PhysicalAttributesFormatter.FormatBlock(
        session.PersonaPhysicalAttributes);
    if (!string.IsNullOrEmpty(personaAppearance))
        sb.AppendLine(personaAppearance);
}
```

### Phase 3: Enhance Prompt To Make Model Use Intimate Attributes

**Option A — Add behavioral guidance to the POV Persona section (data-driven, not hardcoded):**

Add a configurable "attribute guidance" block that tells the model how to use intimate attributes. This should be stored in RP theme data (not hardcoded), for example as an `IntimateAttributeGuidance` field on the theme profile or scenario narrative settings.

Example (for the Husband role with below-average attributes):
```
Sexual Performance Note: This character's intimate attributes describe real limitations. During intimate scenes, portray realistic outcomes: shorter duration, less confident technique, partner may feel underwhelmed or unsatisfied. Do not override these attributes with idealized performance.
```

**Option B — Move persona into "Characters in this scene" section:**

Instead of treating the persona as a separate POV section, include Ken in the "Characters in this scene:" list with his appearance block. This gives equal visual weight to all characters' attributes.

**Option C — Add attribute contrast note:**

When multiple male characters have intimate attributes, inject a comparison note that the model should use to differentiate encounters.

### Phase 4: Consider Gender-Aware Attribute Filtering

Currently `PhysicalAttributesFormatter.FormatBlock()` includes ALL attributes for ALL characters. Consider adding optional gender filtering so:
- Male characters only show Intimate — Male section
- Female characters only show Intimate — Female section
- Shared section always shows

This would prevent confusion if a male character accidentally has female attributes set (or vice versa). However, this is lower priority since null fields are already skipped.

### Phase 5: Add Verification Tests

Add tests that:
1. Verify `PhysicalAttributesFormatter.FormatBlock()` output includes male intimate fields when populated
2. Verify both prompt builders include persona physical attributes
3. Verify prompt text contains expected attribute strings for a known session

---

## Decision Points For Review

1. **Do we want to gender-filter intimate attributes in FormatBlock, or keep the current unconditional approach?** The current approach is simpler but could leak wrong-gender attributes if data is misconfigured.

2. **Should the persona (Ken) be listed in "Characters in this scene:" alongside Becky and Dean?** This would give equal weight to his attributes.

3. **How should we instruct the model to use intimate attributes behaviorally?** Options:
   - Via configurable guidance text on the theme/scenario (data-driven)
   - Via a dedicated prompt section that synthesizes attribute implications
   - Via stronger prompt framing that treats attributes as behavioral constraints

4. **Is the InteractionRetryService fix sufficient, or do we need broader prompt structure changes?** The retry gap is a clear bug; the broader question is whether the main continuation prompt also needs restructuring.

