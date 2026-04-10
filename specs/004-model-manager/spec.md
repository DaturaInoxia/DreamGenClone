# Feature Specification: Model Manager

**Feature Branch**: `004-model-manager`  
**Created**: 2026-04-03  
**Status**: Draft  
**Input**: User description: "Create a Model Manager feature that replaces hardcoded appsettings.json model configuration with a runtime-configurable system supporting LM Studio (local), Together AI, and OpenRouter providers. Users can register models, assign defaults per application function, change models at runtime, and store API keys securely."

## Clarifications

### Session 2026-04-03

- Q: Should models be auto-discovered from provider APIs or manually registered? -> A: Manual registration only. Users enter model identifier and display name.
- Q: Should the Model Manager be a dedicated nav page or a settings modal? -> A: Dedicated page in the navigation.
- Q: Should all application functions support independent model assignment? -> A: Yes. Nine independent functions: RolePlay Generation, Story Mode Generation, Story Summarize, Story Analyze, Story Rank, Scenario Preview, Scenario Adapt, Writing Assistant, RolePlay Assistant.
- Q: How should API keys be stored? -> A: Encrypted in SQLite using DPAPI (Windows Data Protection) behind an IApiKeyEncryptionService interface.
- Q: Should per-session model overrides pull from registered models or remain free-text? -> A: Dropdown populated from registered enabled models.
- Q: Should providers use separate client implementations or a unified client? -> A: Unified OpenAI-compatible client. All three providers use the same /v1/chat/completions endpoint format with different base URLs and optional Bearer token auth.
- Q: Should Story Analysis sub-functions (Summarize, Analyze, Rank) share one model or get independent assignments? -> A: Per-function — each gets its own model and parameter defaults.
- Q: For API key encryption, what protection level is appropriate? -> A: DPAPI (Windows Data Protection). Acceptable for a local single-user Windows app.
- Q: How should the system handle provider-specific HTTP error responses (401, 429, 5xx)? -> A: Map known HTTP status codes to distinct, actionable user-facing error messages with recovery guidance (e.g., 401 → invalid API key, 429 → rate limit, 5xx → server error).
- Q: Should provider timeout be configurable per provider or remain a single global setting? -> A: Per-provider timeout, configurable in the Model Manager UI. Local LM Studio may need 120s+ while cloud providers typically respond in 10-30s.
- Q: Should the system enforce that at least one provider/model must exist, or allow a degraded state? -> A: Seed migration ensures defaults always exist. The error path (no model configured) is sufficient if a user explicitly deletes everything. No additional enforcement needed.
- Q: Should the Model Manager page present all three sections simultaneously or use a guided tab/step layout? -> A: Single scrollable page with all three sections visible (Providers, Models, Function Defaults). Inline validation guides dependency order (e.g., "add a provider first" if none exist).
- Q: What identifying fields should be included in generation call log entries? -> A: Function name, resolved model identifier, provider name, whether a session override was active, and request duration. No prompt content logged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register and Configure AI Providers (Priority: P1)

As a user, I can add AI providers (LM Studio, Together AI, OpenRouter) with their connection details and optionally an API key, so the application can connect to multiple inference sources without editing configuration files.

**Why this priority**: Without at least one configured provider, no model can be used. This is the foundational building block for all other model management features.

**Independent Test**: Can be fully tested by navigating to the Model Manager page, adding a provider with a base URL, clicking "Test Connection" to verify reachability, and confirming the provider appears in the saved list after page refresh.

**Acceptance Scenarios**:

1. **Given** the Model Manager page is open, **When** the user adds a new provider by selecting a provider type (LM Studio, Together AI, or OpenRouter), entering a name, base URL, timeout in seconds, and optionally an API key, and saves, **Then** the provider is persisted in the database and appears in the provider list.
2. **Given** a provider exists with a valid base URL, **When** the user clicks "Test Connection", **Then** the system attempts to reach the provider endpoint and displays a clear success or failure result.
3. **Given** a provider has an API key configured, **When** the provider is saved, **Then** the API key is stored encrypted and is never returned as plaintext in the UI after save (displayed as masked).
4. **Given** a Together AI or OpenRouter provider is added without an API key, **When** the user attempts to test the connection, **Then** the system displays guidance that an API key is required for cloud providers.
5. **Given** a provider exists, **When** the user disables it, **Then** all models under that provider become unavailable for selection in function defaults and session overrides, but their configuration is preserved.
6. **Given** a provider exists with registered models and function assignments, **When** the user deletes the provider, **Then** the system warns about dependent models and function defaults that will be affected and requires confirmation.

---

### User Story 2 - Register Models Under Providers (Priority: P1)

As a user, I can register available models under each provider by entering a model identifier and display name, so the application knows which models are available for use across different features.

**Why this priority**: Models are the core resource referenced by all function defaults and session overrides. Without registered models, no function can be assigned a model.

**Independent Test**: Can be fully tested by adding a provider, then registering a model with an identifier (e.g., "Dolphin3.0-Llama3.1-8B") and display name, and confirming the model appears in the model list grouped under its provider.

**Acceptance Scenarios**:

1. **Given** an enabled provider exists, **When** the user adds a model by entering a model identifier and display name, **Then** the model is persisted and appears in the model list under the correct provider.
2. **Given** models exist under a provider, **When** the user views the Models section, **Then** models are displayed grouped by provider with provider name as the group header.
3. **Given** a model exists, **When** the user disables it, **Then** the model becomes unavailable for selection in function defaults and session overrides, but its configuration is preserved.
4. **Given** a model is currently assigned as the default for one or more functions, **When** the user disables or deletes the model, **Then** the system warns about affected function assignments and requires confirmation.
5. **Given** a model with a duplicate identifier already exists under the same provider, **When** the user tries to add it again, **Then** the system prevents the duplicate and displays a clear message.

---

### User Story 3 - Assign Default Models to Application Functions (Priority: P1)

As a user, I can assign a registered model with specific generation parameters (temperature, topP, maxTokens) as the default for each of the nine application functions, so different features use the most appropriate model without requiring per-session configuration.

**Why this priority**: This replaces the static appsettings.json model configuration and is the core runtime benefit. Once providers and models are registered, function mapping enables the system to route AI calls to the correct model.

**Independent Test**: Can be fully tested by registering a provider and model, navigating to Function Defaults, assigning the model to "RolePlay Generation" with specific parameters, then starting a RolePlay session and verifying the generation call uses the assigned model.

**Acceptance Scenarios**:

1. **Given** the Function Defaults section is open, **When** the user views the grid, **Then** all nine application functions are listed: RolePlay Generation, Story Mode Generation, Story Summarize, Story Analyze, Story Rank, Scenario Preview, Scenario Adapt, Writing Assistant, and RolePlay Assistant.
2. **Given** enabled models exist, **When** the user selects a model for a function from the dropdown, **Then** only enabled models from enabled providers appear in the dropdown.
3. **Given** a model is selected for a function, **When** the user adjusts temperature, topP, and maxTokens sliders, **Then** the parameter values are displayed and persisted when saved.
4. **Given** a function default is saved, **When** any application feature triggers a generation call for that function, **Then** the call uses the assigned model and parameters without requiring an application restart.
5. **Given** a function has no assigned model, **When** a generation call is triggered for that function, **Then** the system displays a clear error indicating that no model is configured for the function and directs the user to the Model Manager.
6. **Given** a function default is changed while a session is active, **When** the next generation call occurs in that session (without a per-session override), **Then** the updated default is used.

---

### User Story 4 - Runtime Model Switching Without Restart (Priority: P1)

As a user, any changes I make to providers, models, or function defaults take effect immediately for subsequent generation calls without restarting the application.

**Why this priority**: This is the primary pain point driving this feature — currently, model changes require editing appsettings.json and restarting. Runtime switching is essential for the feature to deliver its core value.

**Independent Test**: Can be fully tested by assigning Model A to RolePlay Generation, triggering a generation, then switching the default to Model B and triggering another generation, and verifying the second call uses Model B.

**Acceptance Scenarios**:

1. **Given** a function default is changed from Model A to Model B, **When** the next generation call for that function occurs, **Then** Model B is used for the request.
2. **Given** a provider's base URL is updated, **When** the next generation call uses a model from that provider, **Then** the request is sent to the new base URL.
3. **Given** a provider is disabled, **When** a generation call would use a model from that provider, **Then** the system falls back appropriately or displays a clear error indicating the provider is unavailable.
4. **Given** a model is disabled while it is the default for a function, **When** a generation call for that function occurs, **Then** the system displays a clear error indicating the assigned model is unavailable and directs the user to update the configuration.

---

### User Story 5 - Per-Session Model Override in RolePlay (Priority: P2)

As a user, I can override the default model for a specific RolePlay session by selecting from registered models in the workspace settings panel, so I can experiment with different models without changing global defaults.

**Why this priority**: Per-session flexibility builds on the default model system. The existing ModelSettingsPanel already supports per-session overrides but with a free-text field. Upgrading to a registered-model dropdown improves usability and prevents typos.

**Independent Test**: Can be fully tested by opening a RolePlay session, selecting a different model from the settings panel dropdown, generating a continuation, and verifying it uses the session-specific model rather than the function default.

**Acceptance Scenarios**:

1. **Given** the RolePlay workspace settings panel is open, **When** the user views the model selector, **Then** a dropdown lists all enabled models from enabled providers grouped by provider.
2. **Given** a per-session model is selected, **When** a generation call occurs in that session, **Then** the session model is used instead of the function default.
3. **Given** a per-session model is selected and the user clears the selection, **When** a generation call occurs, **Then** the function default model is used.
4. **Given** a per-session model is selected, **When** the user adjusts temperature, topP, or maxTokens in the settings panel, **Then** those parameters apply to the session override.
5. **Given** a per-session override is active in one session, **When** a generation call occurs in a different session, **Then** the other session uses its own override or the function default (overrides are session-scoped).

---

### User Story 6 - Secure API Key Storage (Priority: P1)

As a user, my API keys for Together AI and OpenRouter are stored securely in the local database and never appear in source-controlled files, so credentials are protected from accidental exposure.

**Why this priority**: Security is non-negotiable. API key exposure in git history is a critical risk. This must be in place before cloud providers can be used.

**Independent Test**: Can be fully tested by configuring a Together AI provider with an API key, then inspecting the SQLite database directly to verify the stored value is encrypted (not plaintext), and confirming git status shows no tracked files containing the key.

**Acceptance Scenarios**:

1. **Given** a user enters an API key when configuring a provider, **When** the provider is saved, **Then** the API key is encrypted using DPAPI before being written to SQLite.
2. **Given** a provider with a stored API key is loaded, **When** the provider details are displayed in the UI, **Then** the API key is shown masked (e.g., "sk-****...****") and is never sent as plaintext to the browser.
3. **Given** a provider with an encrypted API key exists, **When** a generation call routes through that provider, **Then** the system decrypts the key at call time and includes it as a Bearer token in the Authorization header.
4. **Given** the SQLite database is opened with an external tool, **When** the API key column is read, **Then** the stored value is encrypted bytes and not human-readable.
5. **Given** a user updates an existing API key, **When** the provider is saved, **Then** the old encrypted value is replaced with the newly encrypted value.

---

### User Story 7 - First-Run Migration from appsettings.json (Priority: P1)

As a user upgrading from the previous configuration system, I expect the application to automatically migrate my existing model settings from appsettings.json into the Model Manager on first run, so the app works identically with zero manual setup.

**Why this priority**: Without seamless migration, upgrading breaks existing users and requires manual reconfiguration. This ensures backward compatibility.

**Independent Test**: Can be fully tested by starting the application with no Model Manager database tables, verifying the migration creates a LM Studio provider, registers Dolphin3.0-Llama3.1-8B and qwen2.5-14b-instruct-1m models, and creates function defaults matching current appsettings behavior.

**Acceptance Scenarios**:

1. **Given** the Model Manager database tables do not exist, **When** the application starts, **Then** the tables are created via migration.
2. **Given** the tables are newly created and empty, **When** migration runs, **Then** a LM Studio provider is seeded with BaseUrl from LmStudio:BaseUrl in appsettings.json, no API key, and enabled status.
3. **Given** the LM Studio provider is seeded, **When** migration completes, **Then** two models are registered: "Dolphin3.0-Llama3.1-8B" (from LmStudio:Model) and "qwen2.5-14b-instruct-1m" (from StoryAnalysis:Model).
4. **Given** models are seeded, **When** migration completes, **Then** function defaults are created matching current behavior: Dolphin3.0-Llama3.1-8B assigned to RolePlay Generation, Story Mode Generation, Scenario Preview, Scenario Adapt, Writing Assistant, and RolePlay Assistant; qwen2.5-14b-instruct-1m assigned to Story Summarize, Story Analyze, and Story Rank; with temperature/topP/maxTokens values matching current appsettings values per function.
5. **Given** migration has already run (tables exist with data), **When** the application starts, **Then** migration does not overwrite user-configured values.

### Edge Cases

- What happens when a provider's base URL becomes unreachable mid-session? The system should return a clear error for that generation attempt and allow the user to retry or switch providers.
- What happens when a cloud provider returns 401 Unauthorized? The system should display an actionable message indicating the API key is invalid or expired and direct the user to update it in the Model Manager.
- What happens when a cloud provider returns 429 Rate Limited? The system should display a message indicating the provider rate limit has been reached and suggest waiting or switching to a different model.
- What happens when a provider returns 5xx Server Error? The system should display a message indicating the provider experienced a server error and suggest retrying or switching models.
- What happens when all models for a function's provider are disabled? The system should display a clear error indicating no model is available and direct the user to the Model Manager.
- What happens when a user deletes the only provider that has models assigned to functions? The system should warn about all affected function defaults and require explicit confirmation. After deletion, affected functions should display clear "no model configured" errors.
- What happens when model identifier is changed at the provider (e.g., the user loads a different model in LM Studio)? The registered model identifier must match what the provider actually serves. The system sends the registered identifier in the request — the provider will error if the model is not loaded. The Test Connection should validate reachability, not model availability.
- What happens when encrypted API key cannot be decrypted (e.g., DPAPI scope change, Windows user profile migration)? The system should log the decryption failure, display a clear error directing the user to re-enter their API key, and not crash.
- What happens when two functions share the same model but with different parameters? Each function default stores its own parameter values independently, so this works correctly without conflict.
- What happens when a user deliberately deletes all providers, models, and function defaults? The system allows this (no enforcement of minimum configuration). All generation calls will fail with the "no model configured" error directing the user to the Model Manager. Seed migration only runs on first start when tables are empty — it does not re-seed after user deletions.

## Requirements *(mandatory)*

### Functional Requirements

#### Provider Management

- **FR-001**: System MUST support three provider types: LM Studio (local, no authentication required), Together AI (cloud, API key required), and OpenRouter (cloud, API key required).
- **FR-002**: System MUST allow users to create, read, update, enable/disable, and delete providers through the Model Manager page.
- **FR-002a**: The Model Manager page MUST display all three sections (Providers, Models, Function Defaults) on a single scrollable page. Each section MUST provide inline validation guidance when a dependency is unmet (e.g., "Add a provider before registering models", "Register a model before assigning function defaults").
- **FR-003**: System MUST store provider details (name, type, base URL, timeout in seconds, API key, enabled status) persistently in SQLite.
- **FR-003a**: System MUST support per-provider timeout configuration. Default timeout values: 120 seconds for LM Studio, 30 seconds for Together AI, 30 seconds for OpenRouter. Users MUST be able to adjust the timeout per provider through the Model Manager UI.
- **FR-004**: System MUST provide a "Test Connection" action per provider that verifies the provider endpoint is reachable and returns a clear success or failure result.
- **FR-005**: System MUST require an API key for Together AI and OpenRouter provider types before allowing model usage.
- **FR-006**: System MUST warn users and require confirmation before deleting a provider that has registered models or active function assignments.

#### Model Registration

- **FR-007**: System MUST allow users to register models under a specific provider by entering a model identifier and display name.
- **FR-008**: System MUST enforce uniqueness of model identifiers within the same provider.
- **FR-009**: System MUST allow users to enable/disable individual models.
- **FR-010**: System MUST display models grouped by their provider in the Models section.
- **FR-011**: System MUST warn users and require confirmation before disabling or deleting a model that is assigned as a function default.

#### Function Default Assignment

- **FR-012**: System MUST support independent model assignment for nine application functions: RolePlay Generation, Story Mode Generation, Story Summarize, Story Analyze, Story Rank, Scenario Preview, Scenario Adapt, Writing Assistant, and RolePlay Assistant.
- **FR-013**: System MUST allow users to set temperature (0.0–2.0), topP (0.0–1.0), and maxTokens (1–8000) per function default.
- **FR-014**: System MUST display only enabled models from enabled providers in function default dropdowns.
- **FR-015**: System MUST persist function default assignments (model, temperature, topP, maxTokens) in SQLite.

#### Unified Completion Client

- **FR-016**: System MUST use a single unified completion client that communicates with all provider types via the OpenAI-compatible /v1/chat/completions endpoint format.
- **FR-017**: System MUST include a Bearer token Authorization header for providers that require an API key (Together AI, OpenRouter).
- **FR-018**: System MUST resolve the correct provider base URL and authentication for each generation call based on the resolved model's provider.
- **FR-018a**: System MUST map well-known provider HTTP error responses to distinct, actionable user-facing error messages: 401 → API key invalid or expired (direct user to Model Manager), 429 → rate limit reached (suggest waiting or switching models), 5xx → provider server error (suggest retry or switch). All other non-success responses MUST surface the HTTP status code with a generic failure message.
- **FR-019**: Together AI requests MUST be sent to the provider's configured base URL (default: https://api.together.ai/v1/chat/completions).
- **FR-020**: OpenRouter requests MUST be sent to the provider's configured base URL (default: https://openrouter.ai/api/v1/chat/completions).
- **FR-021**: LM Studio requests MUST be sent to the provider's configured base URL (default: http://127.0.0.1:1234/v1/chat/completions).

#### Model Resolution

- **FR-022**: System MUST resolve the model for each generation call using the following priority: (1) per-session override if set, (2) function default if configured, (3) error with clear message if neither exists.
- **FR-023**: All existing services that perform generation calls (RolePlayContinuationService, StoryEngineService, StoryAnalysisService, ScenarioAdaptationService, WritingAssistantService, RolePlayAssistantService) MUST use the model resolution service instead of reading model configuration from IOptions.
- **FR-024**: Model resolution MUST take effect immediately without application restart when function defaults or provider configurations are changed.

#### Per-Session Override

- **FR-025**: System MUST provide a model selection dropdown in the RolePlay workspace settings panel populated from enabled registered models.
- **FR-026**: Models in the per-session dropdown MUST be grouped by provider.
- **FR-027**: Per-session model overrides MUST remain in-memory and scoped to the individual session.
- **FR-028**: Per-session overrides MUST include adjustable temperature, topP, and maxTokens parameters.

#### Security

- **FR-029**: System MUST encrypt API keys using DPAPI (System.Security.Cryptography.ProtectedData) before storing in SQLite.
- **FR-030**: System MUST decrypt API keys only at the point of use (HTTP request construction) and MUST NOT cache decrypted keys in memory beyond the request scope.
- **FR-031**: System MUST never return plaintext API keys to the UI after initial save. The UI MUST display masked values (e.g., "sk-****...****").
- **FR-032**: API keys MUST NOT be stored in appsettings.json, appsettings.Development.json, or any file tracked by git.

#### Migration

- **FR-033**: System MUST automatically create Model Manager database tables (Providers, RegisteredModels, FunctionModelDefaults) on first application start if they do not exist.
- **FR-034**: System MUST seed initial data from existing appsettings.json values on first run: create LM Studio provider, register current models, and create function defaults matching current behavior.
- **FR-035**: Seed migration MUST NOT overwrite user-configured data on subsequent application starts.

#### Persistence and Logging

- **FR-036**: All Model Manager data MUST be persisted in SQLite consistent with existing application persistence patterns.
- **FR-037**: Application logging MUST use Serilog with structured message templates and contextual properties aligned with .NET 9 logging best practices.
- **FR-038**: Provider operations (create, update, delete, test connection), model operations (register, enable/disable, delete), function default changes, and model resolution calls MUST emit Information-level logs.
- **FR-038a**: Generation call log entries MUST include as structured properties: function name, resolved model identifier, provider name, whether a session override was active, and request duration in milliseconds. Prompt content MUST NOT be included in log entries.
- **FR-039**: Failed generation calls, decryption failures, and model resolution errors MUST emit Error-level logs with actionable context.
- **FR-040**: Log levels MUST be configurable via settings (including Verbose) without code changes.

### Key Entities

- **Provider**: Represents an AI inference endpoint. Key attributes: unique identifier, user-defined name, provider type (LM Studio, Together AI, or OpenRouter), base URL for the completions endpoint, request timeout in seconds, optionally an encrypted API key, and enabled/disabled status.
- **RegisteredModel**: Represents a specific model available through a provider. Key attributes: unique identifier, reference to its parent provider, model identifier string (used in API requests), user-friendly display name, and enabled/disabled status.
- **FunctionModelDefault**: Represents the default model and generation parameters assigned to an application function. Key attributes: unique identifier, function name (one of nine application functions), reference to the assigned model, and generation parameters (temperature, topP, maxTokens).
- **AppFunction**: Represents the nine application functions that can independently be assigned a default model: RolePlay Generation, Story Mode Generation, Story Summarize, Story Analyze, Story Rank, Scenario Preview, Scenario Adapt, Writing Assistant, RolePlay Assistant.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can add a new AI provider, register a model, assign it to a function, and verify the change takes effect in under 3 minutes without restarting the application.
- **SC-002**: All nine application functions produce successful generation output when configured with appropriate models through the Model Manager.
- **SC-003**: Switching a function's default model takes effect on the very next generation call for that function, with zero delay or restart required.
- **SC-004**: API keys stored in the database are not human-readable when inspected directly in the SQLite file.
- **SC-005**: No API keys appear in any git-tracked files, verified by scanning the repository.
- **SC-006**: First-run migration seeds providers, models, and function defaults from existing appsettings.json values, and the application behaves identically to pre-migration configuration with no manual setup.
- **SC-007**: Per-session model overrides in the RolePlay workspace correctly override the function default for that session only, without affecting other sessions.
- **SC-008**: Disabling a provider immediately makes all its models unavailable for new function assignments and session overrides.
- **SC-009**: Test Connection provides a clear success or failure result within 10 seconds for a reachable provider and within the timeout period for an unreachable provider.
- **SC-010**: A generation call using a Together AI or OpenRouter provider includes the correct Authorization Bearer header derived from the stored encrypted API key.

## Assumptions

- The application runs on Windows. DPAPI (System.Security.Cryptography.ProtectedData) is a Windows-only API and is acceptable for this local single-user application.
- All three provider types (LM Studio, Together AI, OpenRouter) support the OpenAI-compatible /v1/chat/completions endpoint format and accept the same request body structure (messages array, model string, temperature, top_p, max_tokens).
- LM Studio is running locally and does not require authentication. Together AI and OpenRouter require Bearer token authentication.
- The current model settings in appsettings.json (LmStudio:Model, StoryAnalysis:Model, ScenarioAdaptation:Model and their associated temperature/topP/maxTokens values) represent the complete set of defaults that must be migrated.
- Per-session model overrides are ephemeral. They are not persisted to the database and are lost when the session ends or the application restarts.
- The existing ILmStudioClient interface and LmStudioClient implementation will be evolved into a generalized completion client rather than maintained as a separate abstraction.
- The Model fields in LmStudioOptions, StoryAnalysisOptions, and ScenarioAdaptationOptions will remain in appsettings.json as the seed source for first-run migration but will no longer be the authoritative configuration source. After migration, the database is authoritative.
- OpenRouter supports optional HTTP-Referer and X-OpenRouter-Title headers for app attribution. These are not required for functionality and are out of scope for the initial implementation.
- Provider timeout is managed per provider through the Model Manager. The first-run migration seeds the LM Studio provider timeout from the existing LmStudio:TimeoutSeconds value in appsettings.json. Cloud providers default to 30 seconds.

## Scope Boundaries

### In Scope

- Provider CRUD with connection testing
- Manual model registration under providers
- Per-function default model assignment with generation parameters
- Unified OpenAI-compatible completion client
- Model resolution service with session override and function default fallback
- DPAPI-encrypted API key storage in SQLite
- Dedicated Model Manager UI page with Providers, Models, and Function Defaults sections
- Per-session model dropdown in RolePlay workspace settings panel
- First-run migration seeding from appsettings.json
- Refactoring existing services to use model resolution

### Out of Scope

- Auto-discovery of available models from provider APIs
- Model cost tracking or usage analytics
- Multi-user authentication or authorization
- Cross-platform encryption (Linux/macOS DPAPI alternative)
- Streaming response support changes
- Provider timeout migration from LmStudio:TimeoutSeconds (seed migration uses the value as the default for the LM Studio provider)
- OpenRouter app attribution headers (HTTP-Referer, X-OpenRouter-Title)
- Model capability validation (verifying a model supports chat completions before assignment)
