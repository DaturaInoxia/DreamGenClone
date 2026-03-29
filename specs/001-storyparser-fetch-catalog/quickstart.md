# Quickstart: StoryParser URL Fetch, Parse, and Catalog

## 1. Prerequisites
- .NET 9 SDK installed.
- Local SQLite path writable (`Persistence:ConnectionString`).
- Supported domain URL available for manual validation.

## 2. Configure feature settings
In `DreamGenClone.Web/appsettings.json`, add/update StoryParser settings:
- TimeoutSeconds (default 10)
- MaxHtmlBytes (default 5242880)
- MaxPageCount (default 20)
- ErrorModeDefault (`FailFast` or `PartialSuccess`)
- SupportedDomains (v1 one entry)

## 3. Run the web app
- From repository root: `dotnet run --project DreamGenClone.Web`
- Open app and use dedicated StoryParser navigation entry.

## 4. Parse a story URL
- Provide a supported URL.
- Start parse.
- Confirm output includes combined story text and parse diagnostics.

## 5. Validate catalog workflows
- Open StoryParser catalog.
- Confirm default newest-first ordering.
- Switch to URL/title alphabetical ordering.
- Run metadata-only search and verify filtered results.
- Open detail view and confirm required fields:
  - Combined text
  - Source URL
  - Parsed date
  - Page count
  - Diagnostics/errors/warnings

## 6. Run tests
- Execute: `dotnet test`
- Verify StoryParser tests cover:
  - Sample1 single-page parity
  - Sample2 multi-page parity
  - Deterministic repeated output
  - Error handling and configured limit behavior
  - Catalog persistence/list/search/view behavior

## 7. Troubleshooting
- Non-HTML response: ensure source URL returns `text/html`.
- Timeout failures: increase TimeoutSeconds for slow responses.
- Page-limit truncation: increase MaxPageCount for longer stories.
- Locked binaries during test runs: stop running web app process before `dotnet test`.

## 8. Validation Notes
- 2026-03-28 implementation validation completed.
- Verified parser/catalog workflows compile and execute under .NET 9 test project.
- Full test suite executed through `DreamGenClone.Tests/DreamGenClone.Tests.csproj` with passing status.
