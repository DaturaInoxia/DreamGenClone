using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayDiagnosticsRepositoryTests
{
    [Fact]
    public async Task LoadThemeMachineDiagnosticEventsAsync_ReturnsNewestFirstAndHonorsTake()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dreamgenclone-machine-diagnostics-{Guid.NewGuid():N}.db");

        try
        {
            var stateRepository = new RolePlayStateRepository(
                Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));
            var diagnosticsRepository = new RolePlayDiagnosticsRepository(stateRepository);

            var events = new List<ThemeMachineDiagnosticEvent>
            {
                new()
                {
                    EventId = "event-1",
                    SessionId = "session-1",
                    ThemeId = "theme-1",
                    MachineKey = "infidelity-brief-disappearance",
                    DefinitionVersion = 1,
                    EventType = "init",
                    ToStateCode = "PublicBaseline",
                    ReasonCode = "ThemeMachineInitialized",
                    PayloadJson = "{}",
                    OccurredUtc = DateTime.UtcNow.AddMinutes(-3)
                },
                new()
                {
                    EventId = "event-2",
                    SessionId = "session-1",
                    ThemeId = "theme-1",
                    MachineKey = "infidelity-brief-disappearance",
                    DefinitionVersion = 1,
                    EventType = "transition",
                    FromStateCode = "PublicBaseline",
                    ToStateCode = "EncounterInProgress",
                    TransitionId = "t-1",
                    ReasonCode = "ThemeMachineTransitionApplied",
                    PayloadJson = "{}",
                    OccurredUtc = DateTime.UtcNow.AddMinutes(-2)
                },
                new()
                {
                    EventId = "event-3",
                    SessionId = "session-1",
                    ThemeId = "theme-1",
                    MachineKey = "infidelity-brief-disappearance",
                    DefinitionVersion = 1,
                    EventType = "blocked",
                    FromStateCode = "ReintegrationCooldown",
                    ToStateCode = "ReintegrationCooldown",
                    ReasonCode = "ReintegrationCooldownGateBlocked",
                    PayloadJson = "{}",
                    OccurredUtc = DateTime.UtcNow.AddMinutes(-1)
                }
            };

            await stateRepository.SaveThemeMachineDiagnosticEventsAsync(events);

            var loaded = await diagnosticsRepository.LoadThemeMachineDiagnosticEventsAsync("session-1", take: 2);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("event-3", loaded[0].EventId);
            Assert.Equal("event-2", loaded[1].EventId);
            Assert.Equal("ReintegrationCooldownGateBlocked", loaded[0].ReasonCode);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch (IOException)
                {
                    // SQLite can hold the file handle briefly after disposal on Windows.
                }
            }
        }
    }
}
