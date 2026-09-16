# Character Identity Ownership and Cross-Namespace Linking

**Status:** Decision captured for future implementation
**Date:** 2026-09-16
**Related:** B-121 Character Identity Studio, B-124 Reference Model and Asset Manager Shell, B-122 Body-Complete Identity, B-123 LoRA Image Studio

## Current implementation

Identity packs are owned by a single opaque `CharacterProfileId` string. `CharacterImageIdentityPack.CharacterProfileId` is used by `ICharacterImageIdentityService.ListPacksAsync`, Character Studio, identity rendering, and promotion.

Character Studio currently resolves that ID in this order:

1. Match a scenario character ID from `IScenarioService.GetAllScenariosAsync`.
2. If no scenario match exists, fall back to an Asset Manager `SceneAssetType.Character` owner with the same ID.
3. Template/persona characters are not currently resolved to identity packs through `PersonaTemplateId`.

This means equal names do not imply shared identity ownership. A Sam represented in Asset Manager, a scenario character, and a persona/template can have different IDs and therefore different packs.

## Decision

For the current B-121 implementation, the existing owner ID remains authoritative. Do not infer identity ownership by name, description, or template label.

Sam's current pack owner is the Asset Manager character ID:

```text
e781811de41f4595af334b06440ea293
```

The immediate UI should identify the owner namespace and ID clearly.

## Future recommended model

Introduce a canonical identity owner that can link multiple source namespaces:

```text
CharacterIdentityOwner
- Id
- DisplayName
- AssetOwnerId
- ScenarioCharacterIds
- PersonaTemplateIds
- ActiveIdentityPackId
```

Resolution should be:

```text
Asset Manager character / scenario character / persona template
    -> canonical identity owner
    -> identity pack
```

A smaller alternative is explicit link tables between packs and scenario/template IDs, but the canonical owner is preferred because it prevents competing packs for one conceptual character.

## Constraints

- Never match by display name alone.
- Never silently merge an existing scenario character pack with an Asset Manager pack.
- Linking must be an explicit user action with visible owner namespace and IDs.
- An identity pack remains scoped to one canonical owner.
- B-121 promotion continues to create a `FaceOnly` pack; B-122 extends the same owner/pack model to `BodyComplete`.
- Scene rendering and LoRA workflows must resolve packs through the canonical owner once linking exists.

## Future UI

Character Studio/Asset Manager should show:

- Owner type: Scenario character / Asset Manager character / Persona template
- Owner ID
- Linked scenario references
- Linked persona/template references
- Active identity pack
- Explicit `Link character identity` action

## Follow-up work

This is a future identity-foundation/linking feature, not a B-121 promotion bug. Design and implement it under the reference-model/Asset Manager ownership work before relying on cross-scenario or persona reuse.
