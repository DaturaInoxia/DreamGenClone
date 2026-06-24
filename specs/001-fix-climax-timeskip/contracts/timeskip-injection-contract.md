# Time-Skip Directive Injection Contract

## Trigger Conditions

```text
ALL of:
  - CurrentPhase == Climax
  - CurrentEncounterNumber > 1
  - InteractionsInCurrentEncounter == 0
  - TimeSkipPending == true
  - No user-typed Instruction in last 3 interactions
```

## User Instruction Detection

```text
HasRecentUserInstruction(session, windowSize=3):
  session.Interactions
    .TakeLast(3)
    .Any(x => x.ActorName == "Instruction" AND x.GeneratedByCommand is null/empty)
```

## Injection Behavior

```text
IF trigger conditions met AND no user Instruction:
  - First actor (i=0) promptText = time-skip directive
  - First actor PromptIntent = Instruction
  - TimeSkipPending = false
  - Log: MultiEncounterTimeSkipDirectiveInjected

IF user Instruction found:
  - Skip injection
  - TimeSkipPending remains true
  - Log: MultiEncounterTimeSkipSkippedDueToUserInstruction
```

## Directive Text

```text
Close the current encounter naturally. Then advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.
```

- No encounter number
- No "before encounter #N begins" language
- Static text (no interpolation)

## Subsequent Actors

```text
Actors i > 0:
  - PromptIntent = Message
  - promptText = "Describe this same moment from your character's perspective."
```
