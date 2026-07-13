# Contracts

**Feature**: 028-encounter-start-detection

## Decision: No external contracts

This feature is an internal engine change with no new external interfaces:

- **No new APIs**: Detection runs inline during existing turn processing. Enrichment uses existing background job infrastructure.
- **No new CLI commands**: No user-facing commands added.
- **No new UI contracts**: No Razor changes. No new pages or components.
- **No new data contracts**: Uses existing `SemanticEventInferenceRequest`/`SemanticEventInferenceResult` contracts unchanged.

The only interface-visible change is the new `WasEncounterStart` property on `RolePlayInteraction`, which is a domain entity property, not a contract.
