# Data Model: StoryParser URL Fetch, Parse, and Catalog

## Entities

## StoryParseRequest
- Description: Input command to parse story content from a URL.
- Fields:
  - SourceUrl (string, required, absolute URL)
  - ErrorMode (enum: FailFast | PartialSuccess, required)
  - RequestedAtUtc (datetime, required)
- Validation:
  - SourceUrl must be valid absolute HTTP/HTTPS URL.
  - SourceUrl host must match supported domain allowlist for v1.

## ParsedStoryRecord
- Description: Persisted aggregate for one parsing run.
- Fields:
  - Id (string, primary key)
  - SourceUrl (string, required)
  - SourceDomain (string, required)
  - Title (string, optional)
  - ParsedUtc (datetime, required)
  - PageCount (int, required, >= 1)
  - CombinedText (string, required)
  - StructuredPayloadJson (string, required)
  - ParseStatus (enum: Success | PartialSuccess | Failed, required)
  - DiagnosticsSummaryJson (string, required)
- Validation:
  - CombinedText cannot be null when status is Success or PartialSuccess.
  - PageCount cannot exceed configured max page count.

## ParsedStoryPage
- Description: Per-page extraction unit embedded in structured payload.
- Fields:
  - Sequence (int, required, 1-based)
  - PageUrl (string, required)
  - ExtractedText (string, required)
  - IsTerminalPage (bool, required)
  - Warnings (list<string>, optional)
- Validation:
  - Sequence must be strictly increasing with no duplicates.
  - PageUrl must belong to supported domain in v1.

## ParseDiagnostics
- Description: Execution diagnostics for fetch/discovery/extraction/persistence.
- Fields:
  - Errors (list<DiagnosticItem>)
  - Warnings (list<DiagnosticItem>)
  - StartedUtc (datetime, required)
  - CompletedUtc (datetime, required)
- Validation:
  - CompletedUtc must be >= StartedUtc.

## DiagnosticItem
- Description: One warning/error event tied to execution context.
- Fields:
  - Code (string, required)
  - Message (string, required)
  - Severity (enum: Warning | Error, required)
  - PageUrl (string, optional)
  - Stage (enum: Validation | Fetch | Discovery | Extraction | Persistence, required)

## ParsedStoryCatalogEntry
- Description: List/search projection for catalog.
- Fields:
  - Id (string)
  - Title (string, optional)
  - SourceUrl (string)
  - SourceDomain (string)
  - ParsedUtc (datetime)
  - ParseStatus (enum)
  - PageCount (int)
- Validation:
  - Supports metadata-only search fields in v1.

## Relationships
- StoryParseRequest -> ParsedStoryRecord: one request creates at most one persisted record.
- ParsedStoryRecord -> ParsedStoryPage: one-to-many (serialized in structured payload for v1).
- ParsedStoryRecord -> ParseDiagnostics: one-to-one diagnostics set per parse run.
- ParsedStoryRecord -> ParsedStoryCatalogEntry: projection relationship for listing/searching.

## State Transitions

## Parse lifecycle
1. Requested
2. FetchingPage1
3. DiscoveringPagination
4. FetchingNextPages (repeat until terminal or configured limit)
5. ExtractingAndNormalizing
6. Persisting
7. Completed (Success | PartialSuccess | Failed)

## Error-mode behavior
- FailFast:
  - On first failure transition directly to Failed with diagnostics.
- PartialSuccess:
  - Preserve successfully extracted pages, append failure diagnostics, transition to PartialSuccess unless no pages succeeded (then Failed).

## Catalog interaction flow
1. Record persisted
2. Entry appears in list (default newest-first)
3. Metadata search filters list
4. Selecting entry loads ParsedStoryRecord detail
