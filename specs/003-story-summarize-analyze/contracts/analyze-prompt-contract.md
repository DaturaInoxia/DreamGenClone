# Contract: Analyze Prompt

## Interface

**Service**: `IStoryAnalysisService`  
**Method**: `AnalyzeAsync(string parsedStoryId, CancellationToken cancellationToken)`  
**Returns**: `AnalyzeResult` (success/partial/failure, dimension results, per-dimension errors)

## LLM Request Contract — Per Dimension

Four separate LLM calls, one per AnalysisDimension. Each uses the same story text input but different system prompts and JSON schemas.

**Common Parameters**:
- model: from LmStudioOptions.Model
- temperature: 0.3 (configurable via StoryAnalysisOptions.AnalyzeTemperature)
- max_tokens: 800 (configurable via StoryAnalysisOptions.AnalyzeMaxTokens)
- top_p: 0.9

**Story text truncation**: Same rules as summarize contract (MaxStoryTextLength, default 12,000 chars).

---

### Dimension: Characters

**System Message**:
```
You are a literary analyst. Extract the characters from the following story.
Respond ONLY with valid JSON in this exact format:
{
  "characters": [
    { "name": "character name", "role": "protagonist/antagonist/supporting/minor", "description": "brief character description" }
  ]
}
Do not include any text outside the JSON object.
```

**Response Schema**:
```json
{
  "characters": [
    { "name": "string", "role": "string", "description": "string" }
  ]
}
```

---

### Dimension: Themes

**System Message**:
```
You are a literary analyst. Identify the major themes in the following story.
Respond ONLY with valid JSON in this exact format:
{
  "themes": [
    { "name": "theme name", "description": "how this theme manifests in the story", "prevalence": "primary/secondary/minor" }
  ]
}
Do not include any text outside the JSON object.
```

**Response Schema**:
```json
{
  "themes": [
    { "name": "string", "description": "string", "prevalence": "string" }
  ]
}
```

---

### Dimension: Plot Structure

**System Message**:
```
You are a literary analyst. Analyze the plot structure of the following story.
Respond ONLY with valid JSON in this exact format:
{
  "exposition": "description of the story setup and initial situation",
  "risingAction": "description of the building tension and complications",
  "climax": "description of the turning point or peak conflict",
  "fallingAction": "description of events after the climax",
  "resolution": "description of how the story concludes"
}
Do not include any text outside the JSON object.
```

**Response Schema**:
```json
{
  "exposition": "string",
  "risingAction": "string",
  "climax": "string",
  "fallingAction": "string",
  "resolution": "string"
}
```

---

### Dimension: Writing Style

**System Message**:
```
You are a literary analyst. Assess the writing style of the following story.
Respond ONLY with valid JSON in this exact format:
{
  "tone": "description of the overall tone",
  "perspective": "narrative perspective (first person, third person limited, etc.)",
  "pacing": "description of the story's pacing",
  "languageComplexity": "assessment of vocabulary and sentence complexity",
  "notableDevices": ["literary device 1", "literary device 2"]
}
Do not include any text outside the JSON object.
```

**Response Schema**:
```json
{
  "tone": "string",
  "perspective": "string",
  "pacing": "string",
  "languageComplexity": "string",
  "notableDevices": ["string"]
}
```

---

## LLM Response Validation

**Per-dimension validation**:
1. Response must be non-empty after trimming
2. Response must parse as valid JSON
3. JSON must match expected schema for that dimension (required top-level keys present)
4. If JSON is wrapped in markdown code fences (```json ... ```), strip fences before parsing

**Partial-success handling**:
- Each dimension validated independently
- Successfully validated dimensions are persisted
- Failed dimensions recorded as null in StoryAnalyses row with error logged
- AnalyzeResult reports per-dimension success/failure

## Persistence Contract

**Table**: StoryAnalyses  
**Operation**: Upsert (INSERT ... ON CONFLICT(ParsedStoryId) DO UPDATE)  
**Fields persisted**: Id, ParsedStoryId, CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, GeneratedUtc, UpdatedUtc  
**Atomicity**: Persist all successful dimensions in a single upsert; null for failed dimensions  
**Overwrite rule**: Only overwrite if at least one dimension succeeded; preserve previous if all fail
