# Research: Model Manager

**Feature**: 004-model-manager | **Date**: 2026-04-03

## Decision Log

### R-001: Unified vs. Per-Provider Completion Client

**Decision**: Unified completion client (single `ICompletionClient` / `CompletionClient`)

**Rationale**: All three providers (LM Studio, Together AI, OpenRouter) implement the OpenAI-compatible `/v1/chat/completions` endpoint. The request body structure (model, messages, temperature, top_p, max_tokens) and response body structure (choices[].message.content) are identical. The only differences are:
- Base URL (configured per provider)
- Authorization header (none for LM Studio, Bearer token for Together AI / OpenRouter)
- Timeout (per provider: 120s local default, 30s cloud default)

A unified client eliminates code duplication and simplifies the DI graph. Provider-specific configuration (URL, auth, timeout) is resolved at call time from the provider record.

**Alternatives Considered**:
- Per-provider client classes (LmStudioClient, TogetherAiClient, OpenRouterClient): Rejected because all three share the same wire protocol. Would require three near-identical classes.
- Strategy pattern with provider-specific strategies: Rejected as over-engineering — the variation is limited to HTTP headers and URL, which can be parameterized.

---

### R-002: HttpClient Management for Multi-Provider

**Decision**: `IHttpClientFactory` with named clients created per provider, resolved by provider ID.

**Rationale**: The current codebase uses typed `HttpClient` via `AddHttpClient<ILmStudioClient, LmStudioClient>()` which binds a single base URL at DI registration time. Multi-provider requires dynamic base URLs. Options:

1. **IHttpClientFactory with named clients**: Register a base `HttpClient` via `AddHttpClient("CompletionClient")` and configure base URL + timeout per request from the resolved provider.
2. **HttpClient per provider instance**: Create and cache `HttpClient` instances keyed by provider ID. Risk of socket exhaustion without `IHttpClientFactory`.
3. **Single HttpClient with per-request URL**: Set the full URL on each request. Works but loses `HttpClient.BaseAddress` benefits.

Option 1 is the .NET recommended pattern. The `CompletionClient` receives `IHttpClientFactory`, creates a client per call, and sets the `BaseAddress` and `Authorization` header from the provider record. `IHttpClientFactory` manages handler lifetime and connection pooling.

**Alternatives Considered**:
- Keep typed client and switch base URL: Not supported by `HttpClient` after first use. Would require disposing and recreating.
- `HttpClientHandler` pool: Unnecessarily complex for 1-3 providers.

---

### R-003: API Key Encryption Strategy

**Decision**: DPAPI via `System.Security.Cryptography.ProtectedData` with `DataProtectionScope.CurrentUser`

**Rationale**: This is a local single-user Windows desktop app. DPAPI is the simplest and most appropriate encryption mechanism:
- No key management required (tied to Windows user profile)
- No external dependencies
- Encryption at rest in SQLite column (Base64-encoded ciphertext)
- Decryption only at call time within `CompletionClient`

The `IApiKeyEncryptionService` interface allows future replacement with cross-platform alternatives (e.g., ASP.NET Data Protection API) if needed.

**Alternatives Considered**:
- ASP.NET Core Data Protection (`IDataProtector`): More complex setup, requires key ring management. Overkill for local single-user scenario.
- AES with user-supplied passphrase: Worse UX — user must remember another password.
- Azure Key Vault / HashiCorp Vault: Cloud dependency, opposite of local-first.
- Plaintext with file permissions: Insufficient — SQLite file is easily readable.

---

### R-004: Model Resolution Architecture

**Decision**: `IModelResolutionService` with deterministic fallback chain: session override → function default → error

**Rationale**: The resolution chain must be:
1. **Session override**: If the current session has a per-session model override set, use it with its associated parameters.
2. **Function default**: If no session override, look up the configured default for the `AppFunction` from the database.
3. **Error**: If neither exists, return a resolution failure with an actionable message directing the user to the Model Manager page.

Resolution result includes: provider base URL, provider API key (encrypted), model identifier, temperature, topP, maxTokens, provider timeout. This is a value object (`ResolvedModel`) that the `CompletionClient` consumes directly.

The service reads from SQLite on each call. Given the single-user scale (~1 call per user action), caching is not needed initially. If profiling shows overhead, an in-memory cache with invalidation on Model Manager saves can be added.

**Alternatives Considered**:
- Three-level fallback (session → function → global default): Rejected per spec — no "global default" concept. Each function must have an explicit assignment, enforced by the spec's "no model configured" error path.
- Cached resolution with event-based invalidation: Premature optimization for single-user. SQLite reads are sub-millisecond for 3 tables with ~20 rows total.

---

### R-005: Migration Strategy (appsettings.json → SQLite)

**Decision**: Seed migration in `SqlitePersistence.InitializeAsync()` — runs once when tables are empty

**Rationale**: The existing `InitializeAsync()` method already handles table creation with `CREATE TABLE IF NOT EXISTS` and column migration with `ALTER TABLE`. The Model Manager migration follows the same pattern:

1. Create three new tables (`Providers`, `RegisteredModels`, `FunctionModelDefaults`) in `InitializeAsync()`
2. After table creation, check if `Providers` table is empty
3. If empty, read `LmStudioOptions`, `StoryAnalysisOptions`, and `ScenarioAdaptationOptions` from configuration
4. Seed: 1 LM Studio provider, 2 models, 9 function defaults with parameter values matching current appsettings

This approach:
- Runs exactly once (tables created + seeded on first start)
- Does not overwrite user data (checks for empty tables)
- Uses existing patterns (no new migration framework)
- Configuration values remain in appsettings.json as seed source but are no longer authoritative

**Alternatives Considered**:
- Separate migration service: Unnecessary — InitializeAsync already handles this pattern.
- Version-tracked migration table: Overkill for a single migration step. If future migrations are needed, the existing `pragma_table_info()` column-check pattern suffices.
- EF Core migrations: Would require adding Entity Framework as a dependency. The project uses raw ADO.NET (Microsoft.Data.Sqlite) throughout.

---

### R-006: Service Refactoring Pattern

**Decision**: Inject `IModelResolutionService` alongside existing `ILmStudioClient` (renamed to `ICompletionClient`), remove `IOptions<*Options>` model field usage

**Rationale**: Each of the 6 services that make generation calls currently follows this pattern:
```csharp
// Current
private readonly ILmStudioClient _lmClient;
private readonly SomeOptions _options;
private readonly LmStudioOptions _lmOptions;
private readonly string _model;

public Service(ILmStudioClient lmClient, IOptions<SomeOptions> options, IOptions<LmStudioOptions> lmOptions)
{
    _model = _options.Model ?? _lmOptions.Model;
}

// Usage
await _lmClient.GenerateAsync(system, user, _model, _options.Temperature, _options.TopP, _options.MaxTokens, ct);
```

The refactored pattern:
```csharp
// New
private readonly ICompletionClient _completionClient;
private readonly IModelResolutionService _modelResolver;

public Service(ICompletionClient completionClient, IModelResolutionService modelResolver)
{
}

// Usage
var resolved = await _modelResolver.ResolveAsync(AppFunction.StorySummarize, sessionOverride: null, ct);
await _completionClient.GenerateAsync(system, user, resolved, ct);
```

This removes the option-reading pattern entirely. The `ResolvedModel` object carries all needed provider + model + parameter info. Services no longer need `IOptions<LmStudioOptions>` or `IOptions<StoryAnalysisOptions>` for model/parameter selection.

**Alternatives Considered**:
- Keep IOptions for non-model fields (MaxStoryTextLength, etc.): Yes — Options classes are retained for non-model configuration. Only the model/temperature/topP/maxTokens fields become DB-driven.
- Gradual migration with dual paths: Unnecessary complexity. The cutover is clean — replace ILmStudioClient with ICompletionClient and add IModelResolutionService in one pass.

---

### R-007: CompletionClient Interface Design

**Decision**: New `ICompletionClient` interface with `ResolvedModel` parameter instead of individual primitives

**Rationale**: The current `ILmStudioClient.GenerateAsync()` takes model, temperature, topP, maxTokens as separate parameters. The new design passes a `ResolvedModel` value object that includes provider info:

```csharp
public interface ICompletionClient
{
    Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken ct = default);
    Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken ct = default);
    Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? apiKey, CancellationToken ct = default);
}
```

This keeps the client stateless regarding provider configuration — it receives everything it needs per call. The `CheckHealthAsync` method takes explicit parameters for the "Test Connection" feature (since there's no `ResolvedModel` for health checks).

**Alternatives Considered**:
- Keep existing ILmStudioClient signature: Would require the client to internally resolve provider details, coupling it to the resolution service.
- Separate ICompletionClient per provider: See R-001 — rejected due to identical wire protocol.

---

### R-008: Per-Session Override Storage

**Decision**: In-memory, scoped to the existing `ModelSettingsService` (singleton with per-session dictionary)

**Rationale**: The existing `ModelSettingsService` already stores per-session model settings in memory. The refactoring changes:
- Model field: from free-text string to `RegisteredModel` ID (Guid)
- The service provides a `GetSessionOverride(sessionId, AppFunction)` method that returns `ResolvedModel?`
- `IModelResolutionService` calls `ModelSettingsService` first in the fallback chain

Per spec, session overrides are ephemeral and not persisted to the database. The existing in-memory pattern is correct.

**Alternatives Considered**:
- Database persistence for session overrides: Rejected per spec — overrides are intentionally ephemeral.
- Blazor circuit-scoped state: Would lose overrides on circuit reconnection, which is acceptable per spec.

---

### R-009: Model Manager UI Page Layout

**Decision**: Single scrollable page with three sections (Providers, Models, Function Defaults), inline dependency validation

**Rationale**: Per clarification Q12, the user chose a single scrollable page over tabs. The sections have natural top-down dependency:
1. **Providers section**: Add/edit/delete/test providers. Shows cards or table rows.
2. **Models section**: Register models under providers. Shows grouped by provider. Inline message if no providers exist.
3. **Function Defaults section**: Assign models to functions. Grid of 9 rows with dropdowns. Inline message if no models exist.

The page uses existing Bootstrap 5 components consistent with other pages in the application. Each section includes inline CRUD forms (no modal dialogs needed for initial implementation).

**Alternatives Considered**:
- Tabbed layout: User explicitly chose single page.
- Modal dialogs for CRUD: Could add later; inline forms are simpler for initial implementation.
- Wizard/stepper: Over-engineered for 3 sections that users may revisit non-linearly.
