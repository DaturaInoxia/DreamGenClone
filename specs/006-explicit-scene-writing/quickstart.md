# Quickstart: Explicit Scene Writing Directives

**Branch**: `006-explicit-scene-writing` | **Date**: 2026-04-27

## Prerequisites

- .NET 9 SDK
- `dotnet build DreamGenClone.sln` passes clean on the current branch

## Phase 1 — Static Prompt Improvements

Phase 1 requires no database migration and no new services. It is five targeted string-replacement changes. Changes can be verified by running the app and observing prompt output.

### Step 1 — Open AppendEscalationGuidance

File: `DreamGenClone.Web/Application/RolePlay/RolePlayContinuationService.cs` (~line 1342)

Locate:
```csharp
if (phase == "Climax")
{
    sb.AppendLine("- Deliver a decisive high-intensity beat now instead of incremental teasing.");
}
```

Replace with:
```csharp
if (phase == "Climax")
{
    sb.AppendLine("- Do not collapse the scene to a single beat or response. Sustain the scene across multiple turns.");
    sb.AppendLine("- Write physical detail: body position, sensation, pacing, and progression.");
    sb.AppendLine("- Explore the full range of physical intimacy appropriate to the context—kissing, necking, oral contact with various body parts, manual stimulation, oral sex, and penetrative sex in varied positions. Each act deserves dedicated attention; do not skip or summarize.");
    sb.AppendLine("- Move through stages incrementally across turns; do not jump directly to penetration unless continuity has already established that point.");
    sb.AppendLine("- Deliver intensity through physical and sensory specificity, not speed.");
    sb.AppendLine("- If characters are narratively hurried, express urgency through action intensity and dialogue—NOT by compressing or abbreviating the scene itself.");
}
```

### Step 2 — Inject Scene Writing Directive Block

In the same file, immediately after the `AppendEscalationGuidance(...)` call (~line 952), add:

```csharp
// Scene Writing Directive — injects for Explicit/Hardcore intensity outside BuildUp
if (resolvedScale >= (int)IntensityLevel.Explicit && currentPhase != "BuildUp" && intent != PromptIntent.Instruction)
{
    sb.AppendLine("Scene Writing Directive:");
    sb.AppendLine("Core Scene-Writing Principles:");
    sb.AppendLine("- You are writing an adult erotic scene. Write with physical and sensory specificity.");
    sb.AppendLine("- Describe body positioning, physical sensations, movement, and pacing explicitly.");
    sb.AppendLine("- Do not rush to climax. Sustain tension and escalation across multiple paragraphs AND multiple turns.");
    sb.AppendLine("- Each AI response advances physical intimacy by one measured increment. Major transitions—moving from kissing to oral stimulation, from oral to penetration, changing positions—warrant their own turns. Do not cram multiple major acts into a single response.");
    sb.AppendLine("Act Variety and Attention:");
    sb.AppendLine("- Explore the full spectrum of physical intimacy: kissing (mouth, neck, body), licking and sucking (nipples, body, genitals), manual stimulation (hands on body, groin, genitals), oral sex, and penetrative sex in varied positions.");
    sb.AppendLine("- Not every scene requires every act—adapt to context, character dynamics, and established continuity. But each act you DO include receives dedicated, detailed attention.");
    sb.AppendLine("- Vary positions and approaches based on setting, character preferences, and emergent dynamics. Avoid formulaic sequences.");
    sb.AppendLine("Pacing and Urgency:");
    sb.AppendLine("- Narrative urgency (characters in a hurry, limited time, spontaneous encounter) is expressed through action intensity, breathless dialogue, and emotional tone. It does NOT abbreviate the writing.");
    sb.AppendLine("- Even a \"quickie\" scene spans multiple full beats. The characters may be rushed; the prose remains detailed.");
    sb.AppendLine("Continuity Awareness:");
    sb.AppendLine("- Foreplay, teasing, and escalating physical contact must be written in detail before penetration occurs, unless the scene has already passed that point.");
    sb.AppendLine("- Use direct, explicit language appropriate to the resolved intensity level.");
}
```

### Step 3 — Expand Climax Framing Guard

File: `DreamGenClone.Web/Application/RolePlay/RolePlayAssistantPrompts.cs` (~line 98)

Replace:
```csharp
if (phase == "Climax")
{
    guards.Add("Deliver high-intensity culmination consistent with established relational dynamics.");
}
```

With:
```csharp
if (phase == "Climax")
{
    guards.Add("Deliver high-intensity culmination consistent with established relational dynamics.");
    guards.Add("Sustain the scene across multiple turns—do not summarize or collapse the climax into a single response.");
    guards.Add("Describe physical progression with sensory and positional detail.");
    guards.Add("Pacing matters: escalate through the scene across turns, do not resolve it in the first paragraph or first response.");
    guards.Add("Explore varied acts and positions; do not rush to the final act. Let the scene breathe.");
}
```

### Step 4 — Update Explicit/Hardcore Descriptions

File: `DreamGenClone.Domain/StoryAnalysis/IntensityLadder.cs` (~line 47)

Replace:
```csharp
IntensityLevel.Explicit => "Direct and erotic delivery with openly expressed intensity.",
_ => "Maximum intensity with no softening of explicit content."
```

With:
```csharp
IntensityLevel.Explicit => "Direct and explicitly erotic writing. Describe physical acts, body positions, and sensation without euphemism. Explore varied intimate acts with dedicated attention to each. Sustain detailed scene narration across multiple turns.",
IntensityLevel.Hardcore => "Maximum explicitness with no softening. Raw, physically detailed and direct. Explore the full range of sexual acts and positions with exhaustive specificity. Each beat spans full prose; each major transition spans its own turn.",
_ => IntensityLadder.GetDefaultDescription(IntensityLevel.Hardcore)
```

> **Note**: If `Hardcore` is currently the `_` fallthrough, add it as an explicit case. Verify by checking the `Levels` collection in `IntensityLadder.cs`.

### Step 5 — Improve Climax Fallback Guidance

File: `DreamGenClone.Infrastructure/StoryAnalysis/ScenarioGuidanceContextFactory.cs` (~line 57)

Replace:
```csharp
"Climax" => $"Deliver a high-intensity culmination that is explicitly framed around '{scenarioLabel}'.",
```

With:
```csharp
"Climax" => $"Deliver a high-intensity culmination explicitly framed around '{scenarioLabel}'. Write the scene with physical detail and sensory pacing across multiple turns. Explore varied acts—kissing, oral stimulation, manual contact, penetration in varied positions—each receiving dedicated attention. Do not collapse the climax to a summary or single response. If characters are narratively hurried, express urgency through action intensity, not abbreviated writing.",
```

### Verify Phase 1

```powershell
dotnet build DreamGenClone.sln
```

Run the app and start an RP session at Climax phase with Explicit intensity. Check the system prompt output (enable debug prompt display if available) to confirm the Scene Writing Directive block appears and `AppendEscalationGuidance` now outputs the multi-line set.

---

## Phase 2 — Configurable SceneDirective

Phase 2 depends on Phase 1 being shipped and stable.

### Step 6 — Add Property to IntensityProfile

File: `DreamGenClone.Domain/StoryAnalysis/IntensityProfile.cs`

Add after `Description`:
```csharp
public string SceneDirective { get; set; } = string.Empty;
```

### Step 7 — Add PromptSanitizer

Create new file: `DreamGenClone.Web/Application/RolePlay/PromptSanitizer.cs`

```csharp
namespace DreamGenClone.Web.Application.RolePlay;

internal static class PromptSanitizer
{
    private const int MaxSceneDirectiveLength = 2000;

    private static readonly string[] InjectionTokenPrefixes =
    [
        "SYSTEM:", "system:",
        "USER:", "user:",
        "ASSISTANT:", "assistant:",
        "[INST]",
        "</s>",
        "###",
        "<|"
    ];

    public static string SanitizeSceneDirective(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // Truncate to max length
        var truncated = input.Length > MaxSceneDirectiveLength
            ? input[..MaxSceneDirectiveLength]
            : input;

        // Remove null bytes and control chars (keep \n, \r, \t)
        var cleaned = new System.Text.StringBuilder(truncated.Length);
        foreach (var ch in truncated)
        {
            if (ch == '\0' || (ch < '\x20' && ch != '\n' && ch != '\r' && ch != '\t'))
            {
                continue;
            }
            cleaned.Append(ch);
        }

        // Strip injection-token lines
        var lines = cleaned.ToString().Split('\n');
        var filtered = lines
            .Where(line => !InjectionTokenPrefixes.Any(tok => line.TrimStart().StartsWith(tok, StringComparison.Ordinal)))
            .ToList();

        return string.Join('\n', filtered).TrimEnd();
    }
}
```

### Step 8 — Update SQLite Persistence

**File**: `DreamGenClone.Infrastructure/Persistence/SqlitePersistence.cs`

**8a** — Add migration in `InitializeSchemaAsync` (after the existing ToneProfiles phase-offset migration block):
```csharp
var checkSceneDirective = connection.CreateCommand();
checkSceneDirective.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='SceneDirective'";
var hasSceneDirective = Convert.ToInt64(await checkSceneDirective.ExecuteScalarAsync(cancellationToken)) > 0;
if (!hasSceneDirective)
{
    var alterSceneDirective = connection.CreateCommand();
    alterSceneDirective.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN SceneDirective TEXT NOT NULL DEFAULT ''";
    await alterSceneDirective.ExecuteNonQueryAsync(cancellationToken);
    _logger.LogInformation("Migrated ToneProfiles table: added SceneDirective column");
}
```

**8b** — Add helper (after `HasTonePhaseOffsetColumnsAsync`):
```csharp
private static async Task<bool> HasToneSceneDirectiveColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
{
    var check = connection.CreateCommand();
    check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='SceneDirective'";
    return Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0;
}
```

**8c** — Update `SaveToneProfileAsync`: add `SceneDirective = $sceneDirective` to INSERT/UPDATE SQL when column is present.

**8d** — Update `LoadToneProfileAsync` / `LoadAllToneProfilesAsync`: add column to SELECT when present.

**8e** — Update `ReadToneProfile`: read `SceneDirective` column when available; default to `string.Empty` for old rows.

### Step 9 — Update IntensityProfileService

File: `DreamGenClone.Infrastructure/StoryAnalysis/IntensityProfileService.cs`

- `CreateAsync`: add `string sceneDirective = ""` parameter; validate + assign before persistence
- `UpdateAsync`: add `string sceneDirective = ""` parameter; validate + assign to `existing.SceneDirective` before save

Validation (both methods):
```csharp
if (sceneDirective?.Length > 2000)
    throw new ArgumentException("Scene directive must not exceed 2000 characters.", nameof(sceneDirective));
existing.SceneDirective = sceneDirective?.Trim() ?? string.Empty;
```

### Step 10 — Update StoryAnalysisFacade

File: `DreamGenClone.Web/Application/StoryAnalysis/StoryAnalysisFacade.cs`

Pass `sceneDirective` in `CreateIntensityProfileAsync` and `UpdateIntensityProfileAsync` calls, threaded from the Razor component.

### Step 11 — Update Prompt Injection to Use Profile Value

File: `RolePlayContinuationService.cs` — Scene Writing Directive block (from Step 2)

Replace static text injection with:
```csharp
string sceneDirectiveContent;
var sanitizedProfileDirective = PromptSanitizer.SanitizeSceneDirective(resolvedProfile?.SceneDirective);
if (!string.IsNullOrEmpty(sanitizedProfileDirective))
{
    sceneDirectiveContent = sanitizedProfileDirective;
    _logger.LogDebug("Scene Writing Directive: using profile-configured text for intensity {Intensity}", resolvedScale);
}
else
{
    sceneDirectiveContent = GetStaticSceneDirective();
    _logger.LogDebug("Scene Writing Directive: using static default for intensity {Intensity}", resolvedScale);
}
sb.AppendLine("Scene Writing Directive:");
sb.AppendLine(sceneDirectiveContent);
```

Where `GetStaticSceneDirective()` is a private static method returning the multi-line string from Step 2.

### Step 12 — Update ThemeProfiles.razor

File: `DreamGenClone.Web/Components/Pages/ThemeProfiles.razor`

After the existing `Description` textarea block:
```razor
<div class="col-12">
    <label class="form-label">
        Scene Writing Directive
        @if (!IsSceneDirectiveActive)
        {
            <span class="ms-1 text-muted small" title="Scene Writing Directive only applies at Explicit or Hardcore intensity. Switch intensity to enable this field."
                  style="cursor:help;">[?]</span>
        }
    </label>
    <textarea class="form-control"
              rows="5"
              @bind="_toneFormSceneDirective"
              disabled="@(!IsSceneDirectiveActive)"
              maxlength="2000"
              placeholder="Optional. Detailed instructions for how to write explicit scenes at this intensity level. Leave blank to use system default."></textarea>
    @if (!IsSceneDirectiveActive)
    {
        <div class="form-text text-muted">Only active at Explicit or Hardcore intensity.</div>
    }
</div>
```

Add computed property in `@code`:
```csharp
private bool IsSceneDirectiveActive =>
    _toneFormIntensity is IntensityLevel.Explicit or IntensityLevel.Hardcore;
```

Add `_toneFormSceneDirective` string field; populate from loaded profile in `SelectTone`; clear in `StartCreateIntensity`.

### Verify Phase 2

```powershell
dotnet build DreamGenClone.sln
```

1. Open Intensity Profiles page, edit an Explicit profile → `SceneDirective` textarea is enabled.
2. Enter custom text and save → next RP continuation at Climax + Explicit intensity shows the custom text in the prompt.
3. Clear field and save → next RP continuation uses the built-in static directive.
4. Edit a Moderate profile → `SceneDirective` textarea is visible but disabled, with tooltip text.
