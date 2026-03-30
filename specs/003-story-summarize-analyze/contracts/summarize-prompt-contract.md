# Contract: Summarize Prompt

## Interface

**Service**: `IStorySummaryService`  
**Method**: `SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken)`  
**Returns**: `SummarizeResult` (success/failure, summary text, error message)

## LLM Request Contract

**Endpoint**: POST `/v1/chat/completions` (via ILmStudioClient.GenerateAsync)

**Parameters**:
- model: from LmStudioOptions.Model
- temperature: 0.3 (configurable via StoryAnalysisOptions.SummarizeTemperature)
- max_tokens: 500 (configurable via StoryAnalysisOptions.SummarizeMaxTokens)
- top_p: 0.9

**System Message**:
```
You are a literary analyst. Provide a concise synopsis of the following story.
The summary should capture the main characters, central conflict, key plot points, and resolution.
Write 2-3 paragraphs in plain text. Do not use bullet points or headings.
```

**User Message**:
```
Story text:
{storyText}
```

When story text exceeds MaxStoryTextLength (default 12,000 chars):
```
Story text (truncated to first {MaxStoryTextLength} characters — full story is {actualLength} characters):
{truncatedStoryText}
```

## LLM Response Contract

**Format**: Plain text (no JSON wrapping)

**Validation**:
- Response must be non-empty after trimming
- Response must be at least 50 characters (reject trivially short outputs)
- No JSON parsing required

**Error Handling**:
- HTTP failure → return SummarizeResult with error message, do not overwrite existing summary
- Empty/too-short response → return SummarizeResult with validation error
- Timeout → return SummarizeResult with timeout error

## Persistence Contract

**Table**: StorySummaries  
**Operation**: Upsert (INSERT ... ON CONFLICT(ParsedStoryId) DO UPDATE)  
**Fields persisted**: Id, ParsedStoryId, SummaryText, GeneratedUtc, UpdatedUtc  
**Atomicity**: Only persist after successful validation
