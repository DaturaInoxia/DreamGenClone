# Feature Specification: StoryParser URL Fetch, Parse, and Catalog

**Feature Branch**: `001-storyparser-fetch-catalog`  
**Created**: 2026-03-28  
**Status**: Draft  
**Input**: User description: "Create a new feature specification for StoryParser that fetches story HTML from a URL, auto-discovers paginated pages, extracts deterministic cleaned story text, persists parsed stories in SQLite, and supports list/search/view catalog workflows."

## Clarifications

### Session 2026-03-28

- Q: Should StoryParser be treated as a separate feature from the main DreamGenClone feature set while reusing existing UI patterns? -> A: Yes. StoryParser is a separate feature and must reuse the existing DreamGenClone UI.
- Q: How should StoryParser be surfaced in the shared UI shell? -> A: Add a dedicated StoryParser navigation entry in the existing UI shell.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Parse Full Story From URL (Priority: P1)

As a user, I can provide a supported story URL and receive one combined parsed story that includes all discovered pages in order.

**Why this priority**: Parsing and combining story pages is the core product value; catalog workflows depend on successful parsing.

**Independent Test**: Can be fully tested by providing a supported URL with pagination, executing parse, and verifying the combined output includes all discovered pages in correct order with deterministic normalization.

**Acceptance Scenarios**:

1. **Given** a valid supported source URL, **When** the user starts parsing, **Then** the system fetches the first page and auto-discovers subsequent pages using pagination patterns.
2. **Given** discovered pages exist, **When** parsing completes, **Then** the system combines extracted content from all fetched pages into one normalized story output.
3. **Given** the same URL is parsed multiple times with unchanged source pages, **When** each parse completes, **Then** the normalized combined output is deterministic across runs.
4. **Given** the configured maximum page count is reached, **When** additional pages are discoverable, **Then** the system stops according to configured limits and records diagnostics.

---

### User Story 2 - Persist Parsed Story Results (Priority: P2)

As a user, I can rely on parsed story results being stored so they are available later for analysis and viewing.

**Why this priority**: Persistence turns one-time parsing into reusable content for ongoing workflows.

**Independent Test**: Can be fully tested by parsing a URL, verifying a persisted record is created, and retrieving that same record by identifier.

**Acceptance Scenarios**:

1. **Given** a parse operation succeeds, **When** persistence is performed, **Then** the system stores combined story text, structured page data, source URL, parsed timestamp, and page count in SQLite.
2. **Given** parse diagnostics are produced, **When** persistence is performed, **Then** diagnostics, errors, and warnings are stored with the parsed story record.
3. **Given** configured parse behavior is set, **When** parsing runs, **Then** timeout, max page size, max page count, and error mode settings are honored.

---

### User Story 3 - List, Search, and View Parsed Stories (Priority: P3)

As a user, I can list parsed stories, search them by metadata, and open a detailed view for a selected story.

**Why this priority**: Catalog access is required to use parsed results after initial ingestion.

**Independent Test**: Can be fully tested by parsing multiple stories, validating list sort behavior, performing metadata search, and opening a detail view with required fields.

**Acceptance Scenarios**:

1. **Given** parsed story records exist, **When** the user opens the parsed story catalog, **Then** stories are listed newest parsed first by default.
2. **Given** parsed story records exist, **When** the user selects URL/title alphabetical sorting, **Then** the catalog order updates to that user-selected mode.
3. **Given** parsed story records exist, **When** the user searches by metadata, **Then** matching records are returned using metadata-only criteria.
4. **Given** a parsed story is selected, **When** detail view opens, **Then** the system shows combined text, source URL, parsed date, page count, and diagnostics/errors/warnings.
5. **Given** the application shell is visible, **When** the user navigates primary features, **Then** a dedicated StoryParser navigation entry is available and routes to StoryParser catalog/parse workflows.

### Edge Cases

- Input URL is invalid, malformed, or unsupported by the v1 domain selector policy.
- First-page response is non-HTML content.
- HTML document is malformed but still partially parseable.
- Next-page link or expected pagination cue is missing before expected final page.
- Page content size exceeds configured per-page maximum.
- Parse reaches configured page-count ceiling before discovering terminal page.
- Parse timeout occurs before first page finishes.
- Error mode is fail-fast and a later page fails after earlier pages succeeded.
- Error mode is partial-success and one or more later pages fail after earlier pages succeeded.
- Persist operation fails after extraction completed.
- Metadata search query is empty, too broad, or yields no results.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST accept a source URL input and validate it before fetch execution.
- **FR-002**: System MUST fetch HTML from supported source URLs and reject non-HTML responses with actionable feedback.
- **FR-003**: System MUST auto-discover and fetch subsequent pages for the same story using source pagination patterns until terminal conditions are reached.
- **FR-004**: System MUST extract cleaned story text using strict domain-specific selectors for one supported domain in v1.
- **FR-005**: System MUST combine extracted page outputs into a single normalized story output ordered by page sequence.
- **FR-006**: System MUST provide structured per-page output data and flattened combined story text in parse results.
- **FR-007**: System MUST persist parsed story records in SQLite including combined text, structured page output, source URL, parsed date, and page count.
- **FR-008**: System MUST persist parse diagnostics including warnings and errors with each parsed story record.
- **FR-009**: System MUST support catalog listing of all parsed stories with default newest-first ordering.
- **FR-010**: System MUST allow users to switch catalog ordering to source URL/title alphabetical mode.
- **FR-011**: System MUST support metadata-only search in v1 and MUST NOT perform combined-story full-text search.
- **FR-012**: System MUST provide parsed story detail view containing combined text, source URL, parsed date, page count, and diagnostics/errors/warnings.
- **FR-013**: System MUST support configurable timeout with default value of 10 seconds.
- **FR-014**: System MUST support configurable maximum HTML size per page with default value of 5 MB.
- **FR-015**: System MUST support configurable maximum page count per fetch session with default value of 20.
- **FR-016**: System MUST support configurable error mode with fail-fast and partial-success behavior.
- **FR-017**: System MUST implement no automatic retry behavior for fetch or parse failures.
- **FR-018**: In fail-fast mode, system MUST stop processing at first failure and return failure diagnostics.
- **FR-019**: In partial-success mode, system MUST return successfully parsed pages and explicit failure diagnostics for pages that failed.
- **FR-020**: Persisted feature data MUST use SQLite unless this spec explicitly states and justifies a different store (for example session storage, local storage, or another backend store).
- **FR-021**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-022**: Major execution paths across layers/components/services MUST emit Information-level logs and provide actionable failure/error logs.
- **FR-023**: Log levels MUST be configurable via settings (including Verbose) without code changes.
- **FR-024**: StoryParser MUST be implemented as a separate feature scope from the main DreamGenClone feature set while reusing established DreamGenClone UI patterns and visual language.
- **FR-025**: Shared application navigation MUST include a dedicated StoryParser entry that routes users to StoryParser workflows without embedding StoryParser under unrelated feature menus.

### Key Entities *(include if feature involves data)*

- **StoryParseRequest**: Represents user-initiated parse input, including source URL and execution mode context.
- **ParsedStoryRecord**: Represents one persisted parsed story, including identifier, source URL, parsed date, page count, combined text, status, and diagnostics summary.
- **ParsedStoryPage**: Represents one extracted page within a story, including page sequence, page URL, normalized text segment, and page-level diagnostics.
- **ParseDiagnostics**: Represents warnings and errors captured during fetch/discovery/extraction/persistence for traceability and user feedback.
- **ParsedStoryCatalogEntry**: Represents list/search projection fields used by the catalog, including identifier, title (if available), source URL/domain, parsed date, and parse status.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of approved single-page sample fixtures in StoryParser Sample1 produce expected normalized output.
- **SC-002**: 100% of approved multi-page sample fixtures in StoryParser Sample2 produce expected normalized combined output across all discovered pages.
- **SC-003**: Re-running parse for the same unchanged fixture URL three consecutive times yields byte-equivalent normalized combined output in all runs.
- **SC-004**: 100% of successfully parsed stories are retrievable through catalog list, metadata search, and detail view workflows.
- **SC-005**: 100% of detail views for persisted parsed stories display required fields: combined text, source URL, parsed date, page count, and diagnostics/errors/warnings.
- **SC-006**: 100% of tested error scenarios (invalid URL, non-HTML response, malformed HTML, missing pagination cue, configured limits) return actionable diagnostics without silent failure.

## Assumptions

- v1 supports one known target domain with strict selectors and does not include generic cross-domain fallback extraction.
- Pagination discovery is based on source page cues and query-parameter progression patterns represented by sample references.
- Combined-story output is the primary representation for display and analysis; structured per-page output is retained for traceability and validation.
- Metadata-only search is sufficient for v1 catalog workflows; full-text search may be introduced in a later feature.
- Existing layered architecture, options binding, and dependency injection conventions remain the required implementation baseline.
- StoryParser is delivered as a separate feature scope and not as a behavioral extension of existing roleplay/story generation flows.
