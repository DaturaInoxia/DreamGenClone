# Quickstart: Story Summarize & Analyze — Test Scenarios

## Prerequisites

- DreamGenClone application running locally
- LM Studio running at `http://127.0.0.1:1234` with a loaded model
- At least one persisted story in the catalog (from StoryParser feature)

## Scenario 1: Summarize a Story (P1)

1. Navigate to `/storyparser` catalog
2. Click a persisted story to open detail view
3. Click **Summarize** button
4. Verify: progress indicator appears during LLM call
5. Verify: summary text appears in a new "Summary" card section
6. Navigate away and return to the same story
7. Verify: summary is still displayed (persisted, no new LLM call)
8. Click **Summarize** again
9. Verify: summary is regenerated and replaced

**Failure scenario**: Stop LM Studio, click Summarize → verify error message appears and no previous summary is lost.

## Scenario 2: Analyze a Story (P2)

1. Open a persisted story detail view
2. Click **Analyze** button
3. Verify: progress indicator shows analysis is running (may take longer — 4 LLM calls)
4. Verify: four sections appear: Characters, Themes, Plot Structure, Writing Style
5. Verify: Characters section contains structured character entries with names, roles, descriptions
6. Verify: Themes section contains theme entries with prevalence levels
7. Verify: Plot Structure section shows exposition → climax → resolution arc
8. Verify: Writing Style section shows tone, perspective, pacing details
9. Navigate away and return → verify analysis is persisted
10. Click **Analyze** again → verify all dimensions regenerated

**Partial failure scenario**: If one dimension returns malformed JSON, verify other dimensions still display successfully.

## Scenario 3: Manage Ranking Criteria (P2)

1. Navigate to ranking criteria management (from story detail or dedicated route)
2. Click **Add Criterion**, enter name "Romance" with weight 5 → verify saved
3. Add "Action" (weight 3), "Humor" (weight 2) → verify all three listed
4. Edit "Humor" weight to 4 → verify change persisted
5. Delete "Action" → verify removed from list
6. Attempt to save criterion with weight 0 → verify validation error
7. Attempt to save criterion with weight 6 → verify validation error
8. Attempt to save criterion with empty name → verify validation error

## Scenario 4: Rank a Story (P3)

1. Ensure at least one ranking criterion exists (from Scenario 3)
2. Open a persisted story detail view
3. Click **Rank** button
4. Verify: progress indicator appears
5. Verify: per-criterion scores displayed (criterion name, score 1–10, reasoning)
6. Verify: weighted aggregate score displayed
7. Navigate away and return → verify ranking is persisted
8. Add a new criterion, return to story, click **Rank** again → verify new criterion included
9. Verify: ranking result shows the updated criteria snapshot

**No criteria scenario**: Delete all criteria, open story, click Rank → verify message directs user to configure criteria first.

## Scenario 5: Edge Cases

1. **Long story**: Parse a very long story (>12,000 chars), summarize it → verify truncation warning in logs, summary still generated
2. **Empty story**: If a story has no text content → verify appropriate error message for summarize/analyze/rank
3. **Concurrent clicks**: Click Summarize while a summarize is in progress → verify button is disabled/second click ignored
4. **Deleted story**: Open catalog, delete a story in another tab, try to summarize from the first tab → verify appropriate error
