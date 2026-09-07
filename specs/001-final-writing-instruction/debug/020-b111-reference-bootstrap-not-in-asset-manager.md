# B-111 Reference Bootstrap Was Not In Asset Manager

## Report

The B-111 implementation stopped with reference creation and promotion still isolated at
`/reference-bootstrap`, although Asset Manager owns reusable references. The user directed that
the entire plan be completed without stopping.

## Analysis

`ReferenceBootstrapService` already persists batches, requires a non-empty frozen text block for
promotion, uses durable generation jobs, persists candidate status through `ProducedImage`, and
promotes accepted candidates into the existing identity, wardrobe, and location mechanisms.
`AssetStudio.razor` only exposes generic asset library operations, so it does not provide the
required describe, curate, promote reference workflow.

## Plan

Move the existing bootstrap controls into Asset Manager while preserving
`ReferenceBootstrapService` as the sole workflow implementation. Retain its batch links to Review
Deck. Retire the old route only after the Asset Manager surface exposes all target-specific actions.

## Resolution

[ ] pending implementation

## Validated

[ ] pending user confirmation