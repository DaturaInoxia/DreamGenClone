# Contract: Scenario Guidance Contracts (Updated)

**Files affected**:
- `DreamGenClone.Application/RolePlay/RolePlayContracts.cs` — `ScenarioGuidanceRequest`, `ScenarioGuidanceOutput`
- `DreamGenClone.Application/StoryAnalysis/Models/ScenarioEngineContracts.cs` — `ScenarioGuidanceInput`, `ScenarioGuidanceContext`

---

## ScenarioGuidanceRequest (updated)

**Location**: `RolePlayContracts.cs`

```csharp
// BEFORE
public sealed record ScenarioGuidanceRequest(
    ...
    string? HusbandAwarenessProfileId,
    ...
);

// AFTER
public sealed record ScenarioGuidanceRequest(
    ...
    IReadOnlyDictionary<string, string> CharacterEncounterProfileIds,  // charId → profileId
    IReadOnlyList<ScenarioCharacter> Characters,                       // needed for label generation
    ...
);
```

`CharacterEncounterProfileIds` is an empty dictionary when no encounter profiles are bound (never null).

---

## ScenarioGuidanceOutput (updated)

**Location**: `RolePlayContracts.cs`

```csharp
// BEFORE
public sealed record ScenarioGuidanceOutput(
    ...
    string? HusbandAwarenessFrame,
    ...
);

// AFTER
public sealed record ScenarioGuidanceOutput(
    ...
    IReadOnlyDictionary<string, string> CharacterBehavioralFrames,  // characterLabel → frameText
    ...
);
```

`CharacterBehavioralFrames` is an empty dictionary when no frames were generated (never null).

---

## ScenarioGuidanceInput (updated)

**Location**: `ScenarioEngineContracts.cs`

```csharp
// BEFORE
public sealed record ScenarioGuidanceInput(
    ...
    string? HusbandAwarenessProfileId,
    ...
);

// AFTER
public sealed record ScenarioGuidanceInput(
    ...
    IReadOnlyDictionary<string, string> CharacterEncounterProfileIds,
    IReadOnlyList<ScenarioCharacter> Characters,
    ...
);
```

---

## ScenarioGuidanceContext (updated)

**Location**: `ScenarioEngineContracts.cs`

```csharp
// BEFORE
public sealed record ScenarioGuidanceContext(
    ...
    string? HusbandAwarenessFrame,
    ...
);

// AFTER
public sealed record ScenarioGuidanceContext(
    ...
    IReadOnlyDictionary<string, string> CharacterBehavioralFrames,
    ...
);
```

---

## Prompt Injection Pattern (both injection sites)

### Site 1: Early in prompt (RolePlayAssistantPrompts.cs, BuildScenarioGuidanceSection)

```csharp
// BEFORE
if (!string.IsNullOrWhiteSpace(guidance.HusbandAwarenessFrame))
{
    promptBuilder.AppendLine(
        $"HARD CONSTRAINT — Partner Character Behavior (authoritative, overrides all theme notes and guidance): " +
        $"{guidance.HusbandAwarenessFrame}");
}

// AFTER
foreach (var (characterLabel, frameText) in guidance.CharacterBehavioralFrames)
{
    if (!string.IsNullOrWhiteSpace(frameText))
    {
        promptBuilder.AppendLine(
            $"HARD CONSTRAINT — {characterLabel} behavioral frame " +
            $"(authoritative, overrides all theme notes and guidance): {frameText}");
    }
}
```

### Site 2: Immediately before writing directive (RolePlayContinuationService.cs)

```csharp
// BEFORE
if (!string.IsNullOrWhiteSpace(guidanceContext.HusbandAwarenessFrame))
{
    sb.AppendLine(
        $"HARD CONSTRAINT — enforce in this response: {guidanceContext.HusbandAwarenessFrame}");
}

// AFTER
foreach (var (characterLabel, frameText) in guidanceContext.CharacterBehavioralFrames)
{
    if (!string.IsNullOrWhiteSpace(frameText))
    {
        sb.AppendLine(
            $"HARD CONSTRAINT — enforce in this response for {characterLabel}: {frameText}");
    }
}
```

---

## ISqlitePersistence (new methods replacing old profile methods)

**Location**: `DreamGenClone.Infrastructure/Persistence/ISqlitePersistence.cs`

```csharp
// NEW
Task SaveCharacterProfileAsync(CharacterProfile profile, CancellationToken cancellationToken = default);
Task<CharacterProfile?> LoadCharacterProfileAsync(string id, CancellationToken cancellationToken = default);
Task<List<CharacterProfile>> LoadAllCharacterProfilesAsync(CancellationToken cancellationToken = default);
Task<bool> DeleteCharacterProfileAsync(string id, CancellationToken cancellationToken = default);

// DEPRECATED (kept until old tables are dropped)
// Task SaveBaseStatProfileAsync(...)   -- marked [Obsolete]
// Task SaveHusbandAwarenessProfileAsync(...)  -- marked [Obsolete]
// ... etc.
```

---

## DI Registration Changes (Program.cs)

```csharp
// REMOVE
builder.Services.AddScoped<IBaseStatProfileService, BaseStatProfileService>();
builder.Services.AddScoped<IHusbandAwarenessProfileService, HusbandAwarenessProfileService>();

// ADD
builder.Services.AddScoped<ICharacterProfileService, CharacterProfileService>();
builder.Services.AddScoped<IBehavioralFrameGenerator, CharacterBehavioralFrameGenerator>();
```
