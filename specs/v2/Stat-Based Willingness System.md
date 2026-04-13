Feature Summary: Stat-Based Willingness System
Objective
Create a threshold-based willingness system that maps the Desire stat (0-100) to explicitness levels, with contextual prompts guiding the LLM on what sexual acts and behaviors are appropriate for each threshold.

High-Level Requirements
Requirement	Description
Stat-Driven Explicitness	Character willingness to engage in sexual acts is determined by their current Desire stat value
Threshold-Based Scale	Desire values map to explicitness levels (e.g., 0-15: emotional only, 50: vanilla, 70: adventurous, 100: uninhibited)
Persona-Specific Profiles	Different willingness profiles can be created for different personas (e.g., "Married Woman," "College Student," "Exploratory")
Per-Character Tracking	Each character in a session has their own Desire stat that can evolve during gameplay
Dynamic Prompt Injection	When generating responses, the LLM is injected with willingness guidelines appropriate to each character's current Desire value
Customizable Profiles	Users can create/edit willingness profiles with custom thresholds, descriptions, and prompt guidelines
Context-Aware Adjustments	(Optional) Willingness can be influenced by other stats (Connection, Restraint, Dominance) for more nuanced behavior
Core Concepts
1. Desire Stat → Explicitness Mapping
0-100 scale representing character's current level of sexual desire and willingness
Each value range maps to an explicitness level with specific allowed acts
Example for "Married Woman" persona:
0-15: Emotional affection only (cuddling, holding hands)
50: Vanilla marital intimacy (lights-off missionary)
70: Enthusiastic intimacy (doggy style, dirty talk)
100: Uninhibited passion (threesomes, public sex, all fantasies)
2. Willingness Profiles
Reusable templates defining thresholds for a specific persona archetype
Each profile targets a stat (usually Desire) and includes:
Threshold ranges (min/max Desire values)
Explicitness level name (e.g., "Vanilla," "Adventurous," "Extreme")
Description (what this level means for the character)
Prompt guideline (instruction for the LLM)
Example scenarios (sample acts at this level)
3. Session Integration
Sessions select a willingness profile (default to "Married Woman" or user-selected)
Each character's Desire stat is tracked in RolePlaySession.AdaptiveState.CharacterStats
When generating a response for a character, the system:
Looks up the character's current Desire value
Finds the matching threshold in the profile
Injects the willingness guideline into the prompt
The LLM respects these constraints when generating content
4. Dynamic Evolution
Desire stat can change during gameplay based on:
User interactions and story progression
Adaptive state updates (e.g., romantic buildup increases Desire)
Character-specific stat adjustments
As Desire changes, the character's willingness levels automatically adjust
User Experience
For Players/Storytellers:

Select a willingness profile when creating a scenario/session
Observe characters behave according to their current Desire values
Influence Desire through story choices (romantic gestures, tension building, etc.)
Characters naturally progress from emotional intimacy → vanilla → adventurous → uninhibited as Desire increases
For Scenario Creators:

Set default willingness profiles for scenarios
Override per-character Desire baselines
Create custom willingness profiles for specific persona types
Fine-tune thresholds and guidelines for desired narrative tone
Edge Cases & Safeguards
Concern	Safeguard
Hard limits	Willingness guidelines are treated as soft preferences; HardDealBreaker themes still enforce absolute boundaries
Abrupt jumps	Desire changes should be gradual (e.g., +5 to +10 per interaction) to prevent unrealistic behavior swings
Consent	All willingness levels assume enthusiastic consent; even at low Desire, character can choose to act or not
Persona consistency	Profile guidelines should align with character's established personality traits
User override	Players can manually adjust Desire values or ignore guidelines if desired (e.g., for fantasy scenarios)
Data Model (Conceptual)

Apply
StatWillingnessProfile
├── Id
├── Name (e.g., "Married Woman")
├── Description
├── TargetStatName (e.g., "Desire")
└── Thresholds[]
    ├── MinValue / MaxValue
    ├── ExplicitnessLevel (e.g., "Vanilla")
    ├── Description (human-readable)
    ├── PromptGuideline (LLM instruction)
    └── ExampleScenarios[]

Apply
RolePlaySession
├── SelectedWillingnessProfileId
└── AdaptiveState.CharacterStats[]
    └── CharacterStats
        └── Stats["Desire"] = 0-100
Success Metrics
[ ] Characters' willingness aligns with their Desire stat values
[ ] Progression from low to high Desire feels natural and narratively satisfying
[ ] Willingness profiles are easily customizable without code changes
[ ] LLM respects willingness guidelines in generated content
[ ] Integration with existing theme/style/tone systems works smoothly
[ ] Players understand and can influence Desire progression through story choices






