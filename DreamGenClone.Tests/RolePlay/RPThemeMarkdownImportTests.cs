using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RPThemeMarkdownImportTests : IDisposable
{
    private readonly string _databasePath;

    public RPThemeMarkdownImportTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"dreamgen-rptheme-import-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task ImportFromMarkdownAsync_PersistsAiNotesAndFitRules()
    {
        var persistenceOptions = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={_databasePath}"
        });

        var sqlite = new SqlitePersistence(
            persistenceOptions,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await sqlite.InitializeAsync();

        var service = new RPThemeService(persistenceOptions, NullLogger<RPThemeService>.Instance);

        var markdown = """
# Theme Definition: Import Coverage

## Theme Metadata

**ID:** `import-coverage-theme`
**Label:** Import Coverage Theme
**Category:** Power
**Weight:** 4

---

## Description

Import coverage description.

---

## Keywords

**Context:**
- party, event

---

## Stat Affinities

| Stat | Value | Rationale |
|------|-------|-----------|
| **Tension** | +2 | Stress from secrecy |
| **Connection** | -1 | Emotional distance |

---

## Character State Fit Logic

**For the transgressing character (e.g., wife):**
- **Tension ≥ 65:** Indicates stress management under pressure
- **Restraint >= 60:** Helps maintain composure after returning

**For the potentially suspicious partner (e.g., husband):**
- **Connection >= 50:** Baseline trust delays suspicion

**Enhanced Fit for Multiple Disappearances Pattern:**
- Pattern of repeated absences during one event

**Fit Score Formula:**
```
Fit Score = (Wife Tension × 0.25) + (Wife Restraint × 0.25)
```

---

## Scenario Guidance

### Build-Up Phase
- Rising anticipation in social setting

### Committed Phase
- Repeated brief disappearances with excuses

---

## Notes for AI Generation

**Key Scenario Elements to Emphasize:**
- Keep departures brief and plausible

**What to Avoid:**
- Do not allow direct witness by partner

**Interaction Dynamics:**
- Excuses should escalate in complexity

**Scenario Distinction from Related Themes:**
- **Vs. hotel encounters:** same venue constraint is required

**Variations Within This Scenario:**
1. Bathroom pattern
""";

        var results = await service.ImportFromMarkdownAsync([new RPThemeImportFile("import-coverage-theme.md", markdown)]);

        var result = Assert.Single(results);
        Assert.True(result.Imported);

        var theme = await service.GetThemeAsync("import-coverage-theme");
        Assert.NotNull(theme);

        Assert.True(theme!.AIGenerationNotes.Count >= 5);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.KeyScenarioElement);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.Avoidance);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.InteractionDynamics);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.ScenarioDistinction);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.Variation);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.FitPattern);
        Assert.Contains(theme.AIGenerationNotes, x => x.Section == RPThemeAIGuidanceSection.FitFormula);

        Assert.True(theme.FitRules.Count >= 2);
        Assert.Contains(theme.FitRules.SelectMany(x => x.Clauses), c =>
            string.Equals(c.StatName, "Tension", StringComparison.OrdinalIgnoreCase)
            && string.Equals(c.Comparator, ">=", StringComparison.OrdinalIgnoreCase)
            && c.Threshold == 65);
    }

    public void Dispose()
    {
        if (!File.Exists(_databasePath))
        {
            return;
        }

        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Test assertions have completed; a transient handle from provider cleanup can delay deletion.
        }
    }
}
