# Phase 0 Research: StoryParser URL Fetch, Parse, and Catalog

## Decision 1: HTML parsing library

- Decision: Use AngleSharp as the HTML DOM parser for domain-specific selector extraction.
- Rationale: Deterministic DOM traversal, robust malformed HTML tolerance, and CSS-selector support fit strict domain selector requirements better than regex parsing.
- Alternatives considered:
  - HtmlAgilityPack: viable but less aligned with modern selector ergonomics and deterministic pipeline design targeted here.
  - Regex/string parsing only: rejected due to fragility and high maintenance risk for pagination/content extraction.

## Decision 2: Pagination discovery strategy

- Decision: Use domain-specific pagination discovery with query-parameter progression support (`?page=2`, `?page=3`) and explicit terminal conditions.
- Rationale: Matches sample navigation pattern, gives predictable traversal, and enables deterministic page ordering.
- Alternatives considered:
  - Generic "follow next link text" only: rejected due to ambiguity and template variation.
  - Crawl all same-domain links: rejected due to overreach and non-deterministic behavior.

## Decision 3: Output representation

- Decision: Return both structured per-page output and a combined flattened story text artifact.
- Rationale: Structured output supports validation and diagnostics while combined output is optimal for UI display and downstream analysis.
- Alternatives considered:
  - Combined text only: rejected because it loses traceability per page.
  - Structured only: rejected because consumers need a direct display-ready combined representation.

## Decision 4: Persistence shape for v1

- Decision: Persist parsed story records in SQLite as one primary record per parse, containing combined text, structured payload JSON, and diagnostics summary.
- Rationale: Aligns with existing persistence pattern, keeps retrieval simple for list/search/view workflows, and satisfies SQLite-default constitution rule.
- Alternatives considered:
  - Per-page normalized table in v1: deferred to a future feature unless query needs require it.
  - In-memory/session-only persistence: rejected because catalog workflows require durable storage.

## Decision 5: Error handling policy

- Decision: Support configurable error modes: fail-fast and partial-success; do not auto-retry.
- Rationale: Matches explicit feature requirements and preserves deterministic, debuggable behavior.
- Alternatives considered:
  - Always fail-fast: rejected because partial-success is a stated requirement.
  - Automatic retries/backoff: rejected because requirement explicitly excludes retries.

## Decision 6: Catalog search and sorting behavior

- Decision: Provide metadata-only search for v1, default newest-first sorting, and user-selectable URL/title alphabetical sorting.
- Rationale: Satisfies declared scope while minimizing schema/index complexity for v1.
- Alternatives considered:
  - Full-text search over combined story content: deferred by explicit out-of-scope statement.
  - Single fixed sort only: rejected because selectable sort is required.

## Decision 7: Separate feature with shared UI

- Decision: Implement StoryParser as a separate feature scope with a dedicated shared-shell navigation entry.
- Rationale: Preserves feature isolation while maintaining consistent user experience and established UI conventions.
- Alternatives considered:
  - Hidden route only: rejected because discoverability is required.
  - Nest under unrelated menu sections: rejected by requirement.
