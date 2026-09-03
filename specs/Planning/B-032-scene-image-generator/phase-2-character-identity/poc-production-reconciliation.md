# Phase 2 POC-to-Production Reconciliation

**Task:** P2-033  
**Recorded:** 2026-09-02  
**Policy:** Forward-only replacement for new production-schema sessions; no backfill, adapter,
dual read/write, or proof-history rewrite.

## Decision

The existing Scene Asset subsystem remains the shared byte and technical-metadata catalog. Identity,
body, wardrobe, location, and approved-derivative records remain separate versioned semantic
aggregates that reference exact Scene Asset IDs. A Scene Asset does not, by itself, prove semantic
approval, consent, qualification, or production approval.

Existing `CharacterImageIdentityPack` and `SceneImageReferenceAsset` rows remain historical POC
evidence. New production-schema sessions use the production aggregates introduced by P2-034 through
P2-036. No runtime service converts, imports, or falls back to the POC rows.

`SceneImageProductionGroup`, `SceneImageRecord` production-attempt fields,
`ApprovedSceneFrameDecision`, and explicit `SceneImageProductionService.PromoteApprovedFrameAsync`
form the current production-image lineage foundation. Promotion creates a reusable `SceneAsset` that
shares the exact approved bytes and checksum; it does not overwrite the attempt or approval record.

## Replacement Map

| Existing POC surface | Production owner | Forward disposition | Requirements |
|---|---|---|---|
| `SceneAsset` / `SceneAssets` | Shared production asset catalog | Retain and extend in P2-035/P2-036 with immutable content identity, provenance, consent/license, approval/version lineage, use scope, and compatibility metadata. It is the one shared picker/catalog source for identity, body, wardrobe, Phase 3 location/control, and future media. | FR2-002-005, FR2-008, FR2-011, FR2-034-035 |
| `SceneAsset.Kind`, `Type`, association fields | Shared catalog classification | Retain as technical/source classification. Do not treat `Complete`, `Type`, or association JSON as semantic approval or a version aggregate. Replace free-form association ownership with typed aggregate references where production requirements depend on it. | FR2-004-005, FR2-007-009, FR2-034-035 |
| `CharacterImageIdentityPack` | Character identity version aggregate | Preserve POC rows as evidence. Introduce the clean production version that references exact shared asset IDs and carries immutable approval lineage. Do not synthesize production packs from POC rows. | FR2-001, FR2-005-006, FR2-009, FR2-011, FR2-041-045 |
| `SceneImageReferenceAsset` | Shared `SceneAsset` plus typed identity-pack membership/reference binding | Stop creating a second production byte-metadata row. The production identity membership records the exact Scene Asset ID, semantic role, face angle/crop/coverage, ordinal, owner, and approved version. Existing rows remain POC evidence only. | FR2-002-006, FR2-008-011, FR2-020, FR2-041-045 |
| `CharacterImageIdentityPack.CanonicalFaceAssetId` | Exact identity-pack membership bound to a shared asset | Preserve the exactly-one canonical-face invariant, but the production foreign key resolves through typed pack membership to one approved shared Scene Asset version. No nearest-angle or alternate-pack substitution is allowed. | FR2-006, FR2-009-010, FR2-017, FR2-020 |
| POC `FullBody` and `Wardrobe` reference kinds | `CharacterBodyProfileVersion` and `CharacterWardrobeLookVersion` | Replace overloaded identity-pack membership with independently approved body and wardrobe aggregates that reference shared Scene Assets. Attempts bind exact versions. | FR2-005, FR2-007-011 |
| `SceneImageRecord.RenderMode`, `IdentityPackId`, `IdentityPacksJson` | `ProductionIntentSnapshot`, ordered reference bindings, `CompiledMediaRequest`, workload item, and immutable attempt | Retain only for the historical one-off path. New production sessions store typed exact version IDs and ordered bindings before dispatch; JSON pack selections are not a production source of truth. | FR2-009-020, FR2-023-033, FR2-041-042 |
| `IdentityControlledImageRequest` and one-off render job | Model-family compiler plus durable workload/dispatch contracts | Retain as POC transport evidence until feature parity. Do not call it from new production navigation. Production compiles deterministically, persists request and attempt records before transport, and never falls back to the one-off job. | FR2-015-023, FR2-026-033, FR2-041-042 |
| Registered-model identity mechanism fields and `ResolvedIdentityImageModel` | `MediaCapabilityProfile` and exact qualified cell | Preserve as POC configuration evidence. Production qualification is keyed by provider, model/version, workflow, compiler, operation, settings schema, and reference/control layout; no mechanism-wide approval is inferred. | FR2-012-017, FR2-021-022 |
| Frozen identity proof files and scorecards | Qualification evidence run | Retain unchanged. Import only explicit pass/fail facts into capability cells; failed angled cases remain rejected and cannot be reclassified by migration. | FR2-013-014, FR2-022, FR2-043-045 |
| `SceneImageProductionGroup` | Intended approved-frame aggregate for one exact B-100 Moment enrichment and POV | Retain as the lineage and review grouping foundation. New workload/attempt records reference its exact B-100 versions; they do not derive facts from legacy interaction prose. | FR2-018-019, FR2-023-024, FR2-028-031, FR2-036-040 |
| Production fields on `SceneImageRecord` | Transitional production attempt implementation | Preserve current evidence and behavior while P2-036 introduces the complete immutable attempt aggregate. New production code must not fabricate missing lineage for older image rows. | FR2-023, FR2-026-033, FR2-039-042 |
| `ApprovedSceneFrameDecision` | Append-only frame approval decision | Retain. Transport `Complete`, shortlist/reject disposition, and approval remain distinct. Approval references one exact successful image/checksum and never mutates its attempt. | FR2-028-030, FR2-039-040 |
| `PromoteApprovedFrameAsync` and `PromotedApprovedFrame` | Explicit approved-derivative-to-shared-asset promotion boundary | Retain as the only current promotion path. The shared file/checksum is reused, provenance links the approval and attempt, and duplicate promotion is rejected. P2-036 adds the complete derivative aggregate rather than bypassing this boundary. | FR2-011, FR2-028-030, FR2-034-040 |
| `AssetStudio` and identity curation page | Shared Asset Manager and Production Studio | Reuse service behavior and interaction patterns, then replace separate production pickers with the shared typed catalog. Do not expose POC one-off actions in new-session production navigation after feature parity. | FR2-034-042 |

## Required Invariants For Follow-Up Slices

1. P2-034 stamps only newly created sessions and rejects an unstamped/older session before any
   production mutation with explicit create-new-session guidance.
2. P2-035 references shared Scene Asset IDs from immutable approved identity/body/wardrobe versions;
   it does not create another file catalog.
3. P2-036 persists typed production intent, exact ordered references, compiler/capability snapshots,
   workload/items, attempts, derivatives, and review records before provider dispatch.
4. Existing POC and proof records remain readable as historical evidence but are never accepted as
   implicit production inputs.
5. There is one explicit promotion boundary from an approved derivative to reusable Scene Asset.
   Completion, qualification, review, approval, and promotion remain separate states.
6. Missing generation stamps, assets, approved versions, qualified cells, bindings, configuration,
   or lineage fail explicitly. No prompt-only, alternate-reference, model, provider, operation, or
   legacy-record fallback is permitted.

## Sequencing

1. P2-034: install and enforce the clean new-session production-schema generation.
2. P2-035: extend the shared asset contract and add independently versioned body/wardrobe aggregates.
3. P2-036: add capability, intent, compiled request, workload/item, attempt, derivative, review, and
   ordered-binding records.
4. P2-037: prove clean-baseline behavior, immutable state transitions, concurrency, hashes, and
   retention before compiler or provider work begins.
