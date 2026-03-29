# StoryParser Service Contract (Phase 1)

## Purpose
Define application-facing contracts for URL parse execution and parsed-story catalog workflows.

## Interface: IStoryParserService

## Method: ParseFromUrlAsync
- Input: StoryParseRequestDto
- Output: StoryParseResultDto
- Behavior:
  - Validates URL and supported domain.
  - Fetches first page and discovers subsequent pages.
  - Applies configured limits and error mode.
  - Produces combined text and per-page structured output.
  - Persists parsed story record and returns persisted identifier when available.

### StoryParseRequestDto
- SourceUrl: string (required)
- ErrorMode: string (`FailFast` | `PartialSuccess`, optional; defaults from config)

### StoryParseResultDto
- ParsedStoryId: string? (null if persistence not reached)
- SourceUrl: string
- ParseStatus: string (`Success` | `PartialSuccess` | `Failed`)
- CombinedText: string
- PageCount: int
- Pages: ParsedStoryPageDto[]
- Diagnostics: ParseDiagnosticsDto

### ParsedStoryPageDto
- Sequence: int
- PageUrl: string
- Text: string
- IsTerminalPage: bool
- Warnings: string[]

### ParseDiagnosticsDto
- StartedUtc: string (ISO-8601)
- CompletedUtc: string (ISO-8601)
- Errors: DiagnosticItemDto[]
- Warnings: DiagnosticItemDto[]

### DiagnosticItemDto
- Code: string
- Message: string
- Severity: string (`Warning` | `Error`)
- Stage: string (`Validation` | `Fetch` | `Discovery` | `Extraction` | `Persistence`)
- PageUrl: string?

## Interface: IStoryCatalogService

## Method: ListAsync
- Input: StoryCatalogQueryDto
- Output: StoryCatalogEntryDto[]
- Behavior:
  - Returns parsed stories sorted newest-first by default.
  - Supports explicit alternate sort mode `UrlTitleAsc`.

## Method: SearchAsync
- Input: StoryCatalogSearchDto
- Output: StoryCatalogEntryDto[]
- Behavior:
  - Applies metadata-only filtering in v1.
  - Must not search combined story full text in v1.

## Method: GetByIdAsync
- Input: id (string)
- Output: ParsedStoryDetailDto?
- Behavior:
  - Returns full detail for selected parsed story.

### StoryCatalogQueryDto
- SortMode: string (`NewestFirst` default | `UrlTitleAsc`)
- Limit: int?
- Offset: int?

### StoryCatalogSearchDto
- Query: string
- SortMode: string (`NewestFirst` default | `UrlTitleAsc`)

### StoryCatalogEntryDto
- Id: string
- Title: string?
- SourceUrl: string
- SourceDomain: string
- ParsedUtc: string
- ParseStatus: string
- PageCount: int

### ParsedStoryDetailDto
- Id: string
- SourceUrl: string
- ParsedUtc: string
- PageCount: int
- CombinedText: string
- ParseStatus: string
- Diagnostics: ParseDiagnosticsDto

## Persistence Contract Extensions (ISqlitePersistence)
- SaveParsedStoryAsync(record)
- LoadParsedStoryAsync(id)
- LoadParsedStoryCatalogAsync(sortMode, limit, offset)
- SearchParsedStoryCatalogAsync(metadataQuery, sortMode)

## Non-Functional Contract Rules
- Timeouts, page limits, and max HTML size must come from configuration.
- Logging must emit Information-level events at major stages and error-level diagnostics on failures.
- Methods must support CancellationToken.
