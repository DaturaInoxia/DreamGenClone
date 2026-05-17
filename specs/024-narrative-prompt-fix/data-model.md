# Data Model: B-024 Narrative Prompt Fix

No new entities or persistence schema changes are required for this feature.

---

## Interface Change — `IRolePlayContinuationService`

One new method is added to the existing interface (REQ-2):

```csharp
/// <summary>
/// Generates a narrative interaction using the validated narrative pipeline.
/// Equivalent to ContinueAsync with PromptIntent.Narrative but always routes
/// through validation and retry logic. Does not stream.
/// </summary>
Task<RolePlayInteraction> ContinueNarrativeAsync(
    RolePlaySession session,
    string actorName,
    string promptText,
    CancellationToken cancellationToken = default);
```

**Why not a new type**: This is an extension of the existing `IRolePlayContinuationService` contract, not a new domain concept. The `RolePlayInteraction` return type is reused unchanged.

---

## Modified Logic — `AnalyzeNarrativeOutput` (internal, no interface change)

Return type `NarrativeValidationResult` record is unchanged. Logic changes only:

| Field | Change |
|-------|--------|
| `FirstPersonLeakCount` | Now counts matches only in narrator body (quotes stripped first) |
| `ShouldRetry` | Now includes `|| interiorityCount > 0` |

---

## Modified Logic — `BuildNarrativeCorrectionPrompt` (internal, no interface change)

Signature change (internal static method):

```csharp
// Before
private static string BuildNarrativeCorrectionPrompt(string originalPrompt)

// After
private static string BuildNarrativeCorrectionPrompt(string originalPrompt, NarrativeValidationResult analysis)
```

---

## No Persistence, No Configuration, No UI Changes

All changes are in application-layer service code. No migrations, no new appsettings keys, no Razor component changes.
