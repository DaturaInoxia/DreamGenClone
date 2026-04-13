# RP Scenario Recipe - Party Seduction Branching

Date: 2026-04-12
Goal: Husband and wife at a medium-size party; another man attempts to seduce the wife; outcome should depend on choices and character stats.

## What the Current Engine Can and Cannot Do

What works now:
- Character base stats seed session adaptive state.
- Ongoing interactions change stats using keyword heuristics.
- Decision points appear and applying options mutates stats.
- Continuation prompt includes adaptive stats and scenario context.

Current limitation:
- There is no built-in deterministic success/failure gate that directly computes "seduction succeeds" vs "fails" from a custom threshold rule in scenario fields.
- Outcome is currently emergent from prompt guidance + evolving stats + user decisions, not a hard rule evaluator for this scene.

Implication for this run:
- You can strongly bias outcome via stat setup, narrative constraints, and decision choices.
- If you need guaranteed deterministic branch resolution, add a code change request for explicit branch rule evaluation.

## Required Scenario Setup

Use these values in Scenario Editor.

### Scenario Details
- Name: Party Seduction Test
- Description: Social party scene where a third-party suitor pressures attraction boundaries in front of existing relationship commitments.

### Plot
- Plot Description: Husband and wife attend a medium-sized party. A confident guest begins escalating attention toward the wife.
- Conflicts:
  - Attraction vs commitment under social pressure
  - Public ambiguity vs private trust
- Goals:
  - Reach a clear branch outcome by turn 5 to 7
  - Preserve character-consistent behavior under pressure

### Setting
- World Description: Contemporary house party with mixed friend groups, moderate noise, open social movement.
- Environmental Details:
  - Crowded kitchen and lounge areas
  - Intermittent privacy pockets (balcony, hallway)

### Narrative Guidelines
- Keep escalation incremental and choice-driven.
- Reflect internal conflict through observable decisions and reactions.
- Track relationship consequences from each boundary test.
- By turn 7, scene must resolve into one branch: SeductionBlocked or SeductionAccepted.

### Default Story Theme Profile (Simple)
- Profile name: Party Discovery Branch
- Add theme preferences:
  - infidelity-public-discovery: MustHave
  - infidelity-public-facade: StronglyPrefer
  - voyeur: Dislike
- Do not add sharing for this test.

Note:
- This profile should be selected as the scenario Default Story Theme Profile so new sessions inherit it automatically.

### Characters (minimum 3)
1. Wife
- Role: Primary target of seduction attempt
- Perspective: ThirdPersonLimited or FirstPersonInternalMonologue
- Suggested base stats for balanced branching:
  - Desire: 55
  - Restraint: 58
  - Tension: 52
  - Connection: 62
  - Dominance: 50
  - Loyalty: 68
  - SelfRespect: 64

2. Husband
- Role: Relationship anchor and trust/boundary pressure source
- Perspective: ThirdPersonLimited
- Suggested base stats:
  - Desire: 48
  - Restraint: 60
  - Tension: 45
  - Connection: 66
  - Dominance: 52
  - Loyalty: 72
  - SelfRespect: 63

3. Suitor
- Role: External seduction pressure
- Perspective: ThirdPersonExternalOnly
- Suggested base stats:
  - Desire: 70
  - Restraint: 35
  - Tension: 40
  - Connection: 30
  - Dominance: 68
  - Loyalty: 20
  - SelfRespect: 50

## Branch Control Strategy

Use two branch profiles by nudging wife stats at scenario start.

### Profile A - Seduction Likely Accepted
- Wife adjustments from balanced:
  - Desire +12
  - Restraint -12
  - Loyalty -10
  - SelfRespect -6

### Profile B - Seduction Likely Blocked
- Wife adjustments from balanced:
  - Desire -10
  - Restraint +12
  - Loyalty +10
  - SelfRespect +8

## Session Execution Protocol

1. Create RP session from this scenario.
2. Ensure identities include wife, husband, suitor.
3. Run first 3 turns with party framing only.
4. Trigger decision point when offered.
5. For accepted branch bias, choose lean-in when conflict peaks.
6. For blocked branch bias, choose hold-back or seek-connection.
7. Validate by turn 5 to 7:
- SeductionAccepted branch signals:
  - Wife Desire trend up, Restraint trend down, loyalty erosion language appears.
- SeductionBlocked branch signals:
  - Wife Restraint and Loyalty trend up or stable, explicit boundary-setting language appears.

## Recommended Acceptance Checks

- Identity options remain stable across turns.
- Adaptive stat deltas appear after interactions and decision application.
- Decision prompt appears at expected cadence.
- Branch outcome language aligns with selected profile and choices by turn 7.

## Change Request if Deterministic Gate Is Required

Request title: Deterministic branch evaluator for scenario outcomes.

Desired behavior:
- Allow scenario-defined branch rules, for example:
  - SeductionAccepted if Wife.Desire >= 70 and Wife.Restraint <= 45 and Wife.Loyalty <= 55.
  - SeductionBlocked otherwise.
- Surface branch decision as explicit system event.
- Persist branch state and display in diagnostics.
