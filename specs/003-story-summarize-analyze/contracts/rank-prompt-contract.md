# Contract: Rank Prompt

## Interface

**Service**: `IStoryRankingService`  
**Method**: `RankAsync(string parsedStoryId, CancellationToken cancellationToken)`  
**Returns**: `RankResult` (success/partial/failure, per-criterion scores, weighted aggregate, errors)

## Prerequisites

- At least one RankingCriterion must exist (validated before LLM calls)
- ParsedStoryRecord must exist and have non-empty CombinedText

## LLM Request Contract — Per Criterion

One LLM call per RankingCriterion. Each call scores the story against a single criterion.

**Parameters**:
- model: from LmStudioOptions.Model
- temperature: 0.1 (configurable via StoryAnalysisOptions.RankTemperature)
- max_tokens: 200 (configurable via StoryAnalysisOptions.RankMaxTokens)
- top_p: 0.9

**Story text truncation**: Same rules as summarize contract (MaxStoryTextLength, default 12,000 chars).

**System Message**:
```
You are a literary analyst scoring a story against a specific criterion.
Score the story on a scale of 1 to 10, where 1 means the criterion is barely present and 10 means it is a dominant, well-executed element.
Respond ONLY with valid JSON in this exact format:
{
  "score": <integer 1-10>,
  "reasoning": "brief explanation of the score"
}
Do not include any text outside the JSON object.
```

**User Message**:
```
Criterion: {criterionName}

Story text:
{storyText}
```

## LLM Response Contract

**Response Schema**:
```json
{
  "score": 7,
  "reasoning": "string"
}
```

**Validation**:
1. Response must be non-empty after trimming
2. Response must parse as valid JSON
3. JSON must contain "score" as integer in range [1, 10]
4. JSON must contain "reasoning" as non-empty string
5. If JSON is wrapped in markdown code fences, strip fences before parsing
6. Score outside [1, 10] → validation failure for that criterion

**Partial-success handling**:
- Each criterion scored independently
- Successfully validated scores are included in the result
- Failed criteria recorded with error details
- RankResult reports per-criterion success/failure

## Weighted Aggregate Computation

```
weightedAggregate = sum(score_i × weight_i) / sum(weight_i)
```

Where:
- `score_i` = LLM score (1–10) for criterion i
- `weight_i` = user-defined weight (1–5) for criterion i
- Only successfully scored criteria are included in the aggregate
- If no criteria succeeded, aggregation fails entirely

Example:
| Criterion | Weight | Score | Weighted |
|-----------|--------|-------|----------|
| Romance   | 5      | 8     | 40       |
| Action    | 3      | 4     | 12       |
| Humor     | 2      | 6     | 12       |
| **Totals**| **10** |       | **64**   |

Weighted Aggregate = 64 / 10 = **6.4**

## Criteria Snapshot

Before making LLM calls, take a snapshot of all current RankingCriteria. The snapshot is persisted with the ranking result to preserve the exact criteria used, even if criteria are later edited or deleted.

**Snapshot Schema**:
```json
[
  { "criterionId": "abc123", "name": "Romance", "weight": 5 },
  { "criterionId": "def456", "name": "Action", "weight": 3 }
]
```

## Persistence Contract

**Table**: StoryRankings  
**Operation**: Upsert (INSERT ... ON CONFLICT(ParsedStoryId) DO UPDATE)  
**Fields persisted**: Id, ParsedStoryId, CriteriaSnapshotJson, ScoresJson, WeightedAggregate, GeneratedUtc, UpdatedUtc  
**Atomicity**: Persist all successful scores and snapshot in a single upsert  
**Overwrite rule**: Only overwrite if at least one criterion succeeded; preserve previous if all fail

## Ranking Criteria CRUD Contract

**Service**: `IRankingCriteriaService`

| Operation | Method | Validation |
|-----------|--------|------------|
| Create | `CreateAsync(name, weight)` | name non-empty, weight 1–5 |
| List | `ListAsync()` | returns all criteria ordered by UpdatedUtc DESC |
| Update | `UpdateAsync(id, name, weight)` | criterion must exist, same validation as create |
| Delete | `DeleteAsync(id)` | criterion must exist, returns bool |

**Table**: RankingCriteria  
**No cascade**: Deleting a criterion does not affect existing StoryRankingResults (they hold snapshots).
