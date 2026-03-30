# Phase 0 Research: Story Summarize & Analyze

## Decision 1: LLM Prompt Strategy — Separate Focused Calls

- **Decision**: Use separate, focused LLM calls for each operation (1 call for summarize, 4 calls for analyze, N calls for rank where N = number of criteria).
- **Rationale**: Spec explicitly requires accuracy over speed. Smaller local models (Dolphin3.0-Llama3.1-8B, ~8B params) perform better with focused, single-task prompts than multi-task combined prompts. Separate calls also enable partial-success handling per FR-011 and FR-021.
- **Alternatives considered**:
  - Single combined prompt for all analysis dimensions: rejected because smaller models lose accuracy on multi-task prompts and partial-success becomes impossible.
  - Batch all ranking criteria into one call: rejected because per-criterion scoring with explicit focus produces more reliable scores.

## Decision 2: Context Window Management for Long Stories

- **Decision**: Implement character-level truncation with a configurable maximum story text length. Default to 12,000 characters (~3,000 tokens at ~4 chars/token). Include truncation notice in the prompt when text is truncated. Log a warning when truncation occurs.
- **Rationale**: Target model (Dolphin3.0-Llama3.1-8B) typically supports 4K–8K context windows. System prompt + instructions consume ~500–1,000 tokens, leaving ~3,000–7,000 tokens for story text. 12,000 chars is a safe default that works with 4K models while being configurable for larger context windows.
- **Alternatives considered**:
  - Chunked summarization (summarize chunks then summarize summaries): deferred to future feature; adds significant complexity for v1.
  - No truncation (send full text, let model fail): rejected because it causes silent quality degradation or outright failures.
  - Token-level truncation with tiktoken: rejected because it adds a dependency for marginal benefit over character-based estimation.

## Decision 3: LLM Response Format — JSON Structured Output

- **Decision**: Request JSON-formatted responses from the LLM for analyze and rank operations. Summarize returns plain text. All responses are validated before persistence.
- **Rationale**: Constitution Principle V (JSON-In/JSON-Out) requires strict contract enforcement. Analysis dimensions and ranking scores are structured data that must be parsed reliably. Summary is naturally plain text. Existing LmStudioClient already supports JSON deserialization patterns.
- **Alternatives considered**:
  - Plain text responses parsed with regex: rejected due to fragility with varying model outputs.
  - JSON for all operations including summarize: rejected because summary is inherently unstructured text.

## Decision 4: Max Tokens Configuration Per Operation Type

- **Decision**: Use operation-specific max_tokens values passed to ILmStudioClient.GenerateAsync: Summarize = 500 tokens, Analyze (per dimension) = 800 tokens, Rank (per criterion) = 200 tokens. Values are configurable via appsettings.
- **Rationale**: Different operations produce different output sizes. Summaries should be concise (~2–3 paragraphs). Analysis dimensions need more room for structured output. Ranking scores are short JSON payloads. Configurable values allow tuning without code changes.
- **Alternatives considered**:
  - Single global max_tokens: rejected because it would either truncate analysis or waste tokens on ranking.
  - No max_tokens limit: rejected because unconstrained generation wastes time and produces verbose, unfocused output.

## Decision 5: Persistence Schema — Separate Tables vs. Extending ParsedStories

- **Decision**: Create three new SQLite tables (StorySummaries, StoryAnalyses, RankingCriteria, StoryRankings) rather than adding columns to the existing ParsedStories table.
- **Rationale**: Summary, analysis, and ranking are optional enrichments — not all stories will have them. Separate tables avoid nullable column bloat on ParsedStories, keep the existing table stable, and allow independent lifecycle management (e.g., delete ranking without affecting story). RankingCriteria is story-independent by requirement.
- **Alternatives considered**:
  - Add SummaryJson, AnalysisJson, RankingJson columns to ParsedStories: rejected due to schema bloat and lifecycle coupling.
  - Separate SQLite database file: rejected because constitution principle VIII requires default SQLite and the existing single-database pattern is simpler.

## Decision 6: Ranking Score Computation

- **Decision**: LLM scores each criterion on a 1–10 scale. Weighted aggregate = sum(score × weight) / sum(weights). Scores and weights are stored with the result for reproducibility.
- **Rationale**: 1–10 scale gives the LLM sufficient granularity without being unnecessarily precise. Weighted average normalizes across different numbers of criteria. Storing the weight snapshot prevents stale aggregates when criteria change.
- **Alternatives considered**:
  - LLM assigns 1–5 matching the weight scale: rejected because it conflates score scale with weight scale.
  - Unweighted average: rejected because user-defined weights are a core requirement.
  - Store only aggregate without per-criterion scores: rejected because per-criterion visibility is required by spec.

## Decision 7: Concurrency Guard for Same-Story Operations

- **Decision**: Use simple UI-level disabling — disable Summarize/Analyze/Rank buttons while an operation is in progress for that story. No server-side locking.
- **Rationale**: Single-user application with Blazor Server interactive rendering. UI state is authoritative for preventing duplicate requests. Server-side locking adds complexity without benefit in a single-user context.
- **Alternatives considered**:
  - Server-side pessimistic locking per story ID: rejected as over-engineering for single-user scenario.
  - Optimistic concurrency with version checks: rejected for same reason.

## Decision 8: Prompt Temperature Settings

- **Decision**: Summarize and Analyze use temperature 0.3 (low creativity, high consistency). Rank uses temperature 0.1 (near-deterministic scoring). All configurable via appsettings.
- **Rationale**: These are analytical tasks, not creative generation. Lower temperatures produce more consistent, reproducible results across regenerations. Ranking especially benefits from near-deterministic behavior to match constitution principle III.
- **Alternatives considered**:
  - Use default 0.7 from existing GenerateAsync: rejected because creative temperature is inappropriate for analytical tasks.
  - Temperature 0.0 for all: rejected because some variation in analysis phrasing is acceptable and temperature 0.0 can cause repetition issues in some models.
