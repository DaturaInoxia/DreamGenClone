# Data Model: Story Summarize & Analyze

## Entities

### StorySummary

A generated synopsis of a persisted story.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, required | Unique identifier |
| ParsedStoryId | string | FK → ParsedStories.Id, required, unique | Associated story |
| SummaryText | string | required, non-empty | Generated synopsis |
| GeneratedUtc | DateTime | required | Timestamp of generation |
| UpdatedUtc | DateTime | required | Last update timestamp |

**Relationships**: One-to-one with ParsedStoryRecord (via ParsedStoryId).

**Validation Rules**:
- SummaryText must be non-empty after trimming.
- ParsedStoryId must reference an existing ParsedStoryRecord.

**State Transitions**:
- Not exists → Created (first summarize)
- Exists → Replaced (regenerate: delete old, insert new)
- Failed regeneration → Previous state preserved (no overwrite on failure)

---

### StoryAnalysisResult

Structured analysis results for a persisted story across four dimensions.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, required | Unique identifier |
| ParsedStoryId | string | FK → ParsedStories.Id, required, unique | Associated story |
| CharactersJson | string? | nullable | Characters dimension result (JSON) |
| ThemesJson | string? | nullable | Themes dimension result (JSON) |
| PlotStructureJson | string? | nullable | Plot structure dimension result (JSON) |
| WritingStyleJson | string? | nullable | Writing style dimension result (JSON) |
| GeneratedUtc | DateTime | required | Timestamp of generation |
| UpdatedUtc | DateTime | required | Last update timestamp |

**Relationships**: One-to-one with ParsedStoryRecord (via ParsedStoryId).

**Validation Rules**:
- At least one dimension must be non-null (partial-success is valid).
- Each non-null dimension must contain valid JSON.
- ParsedStoryId must reference an existing ParsedStoryRecord.

**State Transitions**:
- Not exists → Created (first analyze, may be partial)
- Exists → Replaced (regenerate: all dimensions re-run)
- Failed regeneration (all fail) → Previous state preserved

**Dimension JSON Schemas**:

Characters:
```json
{
  "characters": [
    { "name": "string", "role": "string", "description": "string" }
  ]
}
```

Themes:
```json
{
  "themes": [
    { "name": "string", "description": "string", "prevalence": "string" }
  ]
}
```

Plot Structure:
```json
{
  "exposition": "string",
  "risingAction": "string",
  "climax": "string",
  "fallingAction": "string",
  "resolution": "string"
}
```

Writing Style:
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

### AnalysisDimension (Enum)

Identifies the four analysis dimensions.

| Value | Description |
|-------|-------------|
| Characters | Character extraction |
| Themes | Theme identification |
| PlotStructure | Plot structure analysis |
| WritingStyle | Writing style assessment |

---

### RankingCriterion

A user-defined criterion for scoring stories. Persisted independently of stories.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, required | Unique identifier |
| Name | string | required, non-empty | Human-readable criterion label |
| Weight | int | required, 1–5 | Relative importance (5 = most valued) |
| CreatedUtc | DateTime | required | Creation timestamp |
| UpdatedUtc | DateTime | required | Last update timestamp |

**Relationships**: None (independent entity). Referenced by StoryRankingResult snapshots.

**Validation Rules**:
- Name must be non-empty after trimming.
- Weight must be an integer in range [1, 5].
- Duplicate names are allowed (no uniqueness constraint on Name).

**State Transitions**:
- Not exists → Created (user adds criterion)
- Exists → Updated (user edits name or weight)
- Exists → Deleted (user removes criterion)

---

### StoryRankingResult

The outcome of ranking a story against a set of criteria.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string (GUID) | PK, required | Unique identifier |
| ParsedStoryId | string | FK → ParsedStories.Id, required, unique | Associated story |
| CriteriaSnapshotJson | string | required | JSON snapshot of criteria used at ranking time |
| ScoresJson | string | required | Per-criterion scores as JSON array |
| WeightedAggregate | double | required | Computed weighted average score |
| GeneratedUtc | DateTime | required | Timestamp of generation |
| UpdatedUtc | DateTime | required | Last update timestamp |

**Relationships**: One-to-one with ParsedStoryRecord (via ParsedStoryId).

**Validation Rules**:
- CriteriaSnapshotJson must be valid JSON array with at least one criterion.
- ScoresJson must be valid JSON array matching criteria count.
- WeightedAggregate = sum(score × weight) / sum(weights).
- ParsedStoryId must reference an existing ParsedStoryRecord.

**State Transitions**:
- Not exists → Created (first rank)
- Exists → Replaced (re-rank after criteria changes)
- Failed re-rank (all fail) → Previous state preserved

**CriteriaSnapshotJson Schema**:
```json
[
  { "criterionId": "string", "name": "string", "weight": 1 }
]
```

**ScoresJson Schema**:
```json
[
  { "criterionId": "string", "name": "string", "score": 7, "weight": 3, "reasoning": "string" }
]
```

Score range: 1–10 (integer). Weight range: 1–5 (from criterion snapshot).

---

## SQLite Tables

### StorySummaries

```sql
CREATE TABLE IF NOT EXISTS StorySummaries (
    Id TEXT PRIMARY KEY,
    ParsedStoryId TEXT NOT NULL UNIQUE,
    SummaryText TEXT NOT NULL,
    GeneratedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_StorySummaries_ParsedStoryId ON StorySummaries (ParsedStoryId);
```

### StoryAnalyses

```sql
CREATE TABLE IF NOT EXISTS StoryAnalyses (
    Id TEXT PRIMARY KEY,
    ParsedStoryId TEXT NOT NULL UNIQUE,
    CharactersJson TEXT NULL,
    ThemesJson TEXT NULL,
    PlotStructureJson TEXT NULL,
    WritingStyleJson TEXT NULL,
    GeneratedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_StoryAnalyses_ParsedStoryId ON StoryAnalyses (ParsedStoryId);
```

### RankingCriteria

```sql
CREATE TABLE IF NOT EXISTS RankingCriteria (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Weight INTEGER NOT NULL CHECK(Weight >= 1 AND Weight <= 5),
    CreatedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);
```

### StoryRankings

```sql
CREATE TABLE IF NOT EXISTS StoryRankings (
    Id TEXT PRIMARY KEY,
    ParsedStoryId TEXT NOT NULL UNIQUE,
    CriteriaSnapshotJson TEXT NOT NULL,
    ScoresJson TEXT NOT NULL,
    WeightedAggregate REAL NOT NULL,
    GeneratedUtc TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL,
    FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_StoryRankings_ParsedStoryId ON StoryRankings (ParsedStoryId);
CREATE INDEX IF NOT EXISTS IX_StoryRankings_WeightedAggregate ON StoryRankings (WeightedAggregate DESC);
```

## Entity Relationship Diagram

```
ParsedStoryRecord (existing)
  1 ─── 0..1  StorySummary
  1 ─── 0..1  StoryAnalysisResult
  1 ─── 0..1  StoryRankingResult

RankingCriterion (independent)
  * ── snapshot ──> StoryRankingResult.CriteriaSnapshotJson
```
