# Debug Record 015: Moment Enrichment Spatial `before` False Positive

## Report

The first Moment Enrichment for Turn 9 / Beat 3 failed with:

`scene_moment_enrichment_output_invalid: Moment enrichment character 'Becky' action or observation describes sequential before/after/then action instead of one frozen state.`

Target lineage:

- Catalogue: `f0a98ca5-c92e-4887-b607-1b63f76597fd`
- Beat: `b3`
- Beat Production plan: `0e5ef4bd-cb41-4fa1-ba52-238efab5ad97`, version 14
- Moment: `m1`
- Latest failed enrichment revision observed during the parser investigation: 4

## Analysis

The persisted model value was:

`Unaware of the door opening, standing bare before the shower stall`

`SceneMomentEnrichmentParser.ValidateFrozenText` treated every occurrence of ` before ` as a temporal sequence marker. In this value, `before the shower stall` is spatial, not a before-and-after action. The validator therefore rejected a valid frozen state.

The parser is the owning decision point. The enrichment contract already instructs the model to describe one instant and reject transitions; no permissive output rewriting or fallback path is appropriate.

## Plan

1. Keep direct sequence markers (`then`, `followed by`, `transitions to`, `moves from`, and `and then`) strict.
2. Recognize `before` and `after` as sequential only when followed by an action form, such as `before turning`.
3. Preserve the existing strict validation error for genuine temporal sequences.
4. Add a regression test for the reported spatial wording.

## Resolution

Updated `SceneMomentEnrichmentParser.ValidateFrozenText` to remove unconditional `before`/`after` substring matching and add a compiled temporal-action phrase check for `before`/`after` followed by an action gerund. Spatial phrases such as `standing bare before the shower stall` now remain valid; temporal prose such as `looks away before turning back` remains invalid.

Added `Parse_AllowsSpatialBeforePhraseInFrozenAction` to `SceneMomentEnrichmentParserTests`.

The subsequent durable retry exposed a separate provider-adherence failure: the model
returned wider Beat-cast profile `p1` even though the selected Moment cast contained
only `p0`. The prompt was strengthened with a final exact-cardinality instruction, and
the response schema was made snapshot-specific: `characters.minItems` and
`characters.maxItems` equal the selected cast count, while `profileKey` is an enum of
the selected profile keys. The job handler now supplies those frozen keys when it
creates the runtime schema.

## Validation

- Focused source-resolver, production-parser, enrichment contract, and enrichment parser tests: 40 passed, 0 failed.
- `ResolveExactSpan` now rejects semantic `exactText` mismatches, accepts line-ending and boundary-whitespace representation differences, and always returns the immutable evidence substring.
- Web project build after all changes: succeeded.
- Source diagnostics: no errors in the changed enrichment contract or job handler.
- A full RolePlay rerun was started after the resolver fix but the terminal runner canceled it at its 153-second execution limit before a test summary was produced; it is therefore inconclusive rather than a failure.

## Runtime Status

The Development host started successfully on `http://localhost:5003`. The valid session is
`4f2eec18-b190-4beb-ad35-8d520ae5c800`, with the shower Studio interaction
`0de64198-6633-41f6-9bcf-9623e2b04345`. The real Blazor UI selected Beat 3 and Moment 1,
created revision 9, and completed Moment Enrichment. Persisted output contains only
Becky (`p0`), sound cue `sfx1`, and video role `VideoStart`; the production controls
unlocked. The earlier `bda4f5c0-ce31-4a67-800f-109892d0e0d0` route was the unrelated
fire-pit interaction and caused the misleading catalogue mismatch.

Validation status: [x] parser regression fixed; [x] selected-cast schema enforcement
fixed; [x] durable UI completion confirmed; [x] exact source-span validation fixed;
[ ] broader RolePlay suite requires a runner with more than the 153-second execution
limit for a conclusive result.
