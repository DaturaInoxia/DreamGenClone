# Pacing Directive Changes — Before/After Reference

**Date**: 2026-07-09
**Branch**: 028-encounter-start-detection
**Reason**: Fast pacing broke after removing time-advancement language. Slow pacing caused repetition ("stay in same beat"). Medium had minor "don't move" signals.

---

## Files Changed

| File | Sections Changed |
|------|-----------------|
| `DreamGenClone.Web/Application/RolePlay/Injectors/EscalationInjector.cs` | Slow, Fast, Medium branches |
| `DreamGenClone.Web/Application/RolePlay/Injectors/SceneTimeDirectionInjector.cs` | Slow (!hasTimeShift), Slow (hasTimeShift), Fast (hasTimeShift), Medium (hasTimeShift) |

---

## EscalationInjector.cs

### Slow

```diff
 case ScenePacing.Slow:
-    sb.AppendLine("- Advance within the same beat — deepen, do not leap.");
-    sb.AppendLine("- Fill the response with sensory, emotional, and physical detail specific to this moment.");
-    sb.AppendLine("- Do not describe a new beat or position.");
+    sb.AppendLine("- Cover exactly one beat this response — richly detailed, deeply explored.");
+    sb.AppendLine("- Fill the response with sensory, emotional, and physical detail specific to this beat.");
+    sb.AppendLine("- Advance to a new beat next response. Do not repeat or re-describe the same beat.");
     break;
```

**Why**: "Do not describe a new beat" + "Advance within the same beat" = LLM freezes on one beat, re-describing it turn after turn. New: one beat per turn, richly detailed, but must advance to a new beat each response.

---

### Fast

```diff
 case ScenePacing.Fast:
-    sb.AppendLine("- This is a fast-paced scene. Pack maximum density into this moment — more actions, reactions, and physical beats.");
-    sb.AppendLine("- Expand each beat before moving to the next. Do not write only one beat when multiple beats fit naturally.");
+    sb.AppendLine("- This is a fast-paced encounter. Move through the full arc rapidly — initiation, act, climax, conclusion — within this and the next response.");
+    sb.AppendLine("- Do not linger on individual beats. Cover the essential actions efficiently and keep moving forward.");
+    sb.AppendLine("- Prioritize forward momentum over detailed description. This is meant to be brief and urgent.");
     break;
```

**Why**: "Pack density into this moment" + "Expand each beat" = LLM lingers on details, making "quickie" encounters take more turns with more positions. "this moment" (singular) prevents arc progression. New: full encounter arc in 2-3 responses, compress, don't linger.

---

### Medium

```diff
 default: // Medium
     sb.AppendLine("- Advance the scene with forward momentum within the current scene.");
-    sb.AppendLine("- Cover one to two beats this response. Avoid repeating only hesitant or reset beats.");
+    sb.AppendLine("- Cover one to two beats this response. Each response should advance to new beats — do not repeat previous beats.");
     break;
```

**Why**: Original was decent ("avoid repeating") but could be clearer. New: explicit "advance to new beats each response."

---

## SceneTimeDirectionInjector.cs

### Slow — !hasTimeShift (TimeShift=None)

```diff
 case ScenePacing.Slow:
-    sb.AppendLine("- Stay in the current moment. Do not skip forward.");
-    sb.AppendLine("- Savor the moment with detailed sensory and emotional depth. One beat per response.");
+    sb.AppendLine("- Cover one beat per response with detailed sensory and emotional depth.");
+    sb.AppendLine("- Advance to a new beat each response. Do not repeat or linger on a previous beat.");
     break;
```

**Why**: "Stay in the current moment" = don't ever move. New: one beat per response but advance each turn.

---

### Slow — hasTimeShift (TimeShift=Small/Medium/Large)

```diff
 case ScenePacing.Slow:
-    sb.AppendLine("- Stay in this moment. Expand one beat with depth before moving to the next.");
-    sb.AppendLine("- Do not jump forward in time. Let the scene breathe and unfold naturally.");
+    sb.AppendLine("- Cover one beat per response, richly expanded with sensory and emotional depth.");
+    sb.AppendLine("- Move to a new beat each response. Let the scene unfold naturally but keep advancing.");
     break;
```

**Why**: "Stay in this moment" = same freeze problem. "Do not jump forward in time" kept as implicit (it's in the TimeShift context). New: advance each response while keeping natural pacing.

---

### Fast — hasTimeShift

```diff
 case ScenePacing.Fast:
-    sb.AppendLine("- Pack this moment with maximum density — more actions, reactions, and physical beats within the current scene.");
-    sb.AppendLine("- Cover the full arc of what's happening right now: initiate, escalate, conclude, react. Expand each beat before moving to the next.");
-    sb.AppendLine("- Do not jump to a different time or setting. Stay in this moment and exhaust it.");
+    sb.AppendLine("- Cover the full arc rapidly: initiate, escalate, conclude, react — all within this response and the next.");
+    sb.AppendLine("- Do not linger on any single beat. Compress the action into efficient, urgent prose.");
+    sb.AppendLine("- Do not jump to a different time or setting. Stay within the current scene but move through it quickly.");
     break;
```

**Why**: "Stay in this moment and exhaust it" = Slow pacing language, directly contradicts Fast intent. "Expand each beat" = linger. New: rapid arc, don't linger, stay in scene but move through quickly.

---

### Medium — hasTimeShift

```diff
 default: // Medium
-    sb.AppendLine("- Expand the current moment with one to two beats — dialogue, actions, reactions. Do not skip forward.");
-    sb.AppendLine("- Let transitions feel organic within the scene. Do not leap to a different time or setting.");
+    sb.AppendLine("- Cover one to two beats this response — dialogue, actions, reactions. Advance to new beats each response.");
+    sb.AppendLine("- Let transitions feel organic within the scene. Do not leap to a different time or setting.");
     break;
```

**Why**: "Do not skip forward" could be interpreted as "stay put." New: explicit "advance to new beats" while keeping time-shift guardrail.

---

## Unchanged Sections

| File | Section | Reason |
|------|---------|--------|
| `SceneTimeDirectionInjector.cs` | Fast — `!hasTimeShift` | Already correct: "Compress multiple beats into one response. Cover more story ground." |
| `SceneTimeDirectionInjector.cs` | Medium — `!hasTimeShift` | Already correct: "Let the scene breathe without dragging. Cover one to two beats." |
| `FinalDirectiveInjector.cs` | Fast HC | Already correct: "Cover more story ground per response — compress multiple beats into one. Advance through the full arc toward its natural resolution." |
| Both injectors | `ShouldFire` methods | Unchanged |
| Both injectors | `Id` and `Priority` | Unchanged |

---

## Design Principle

**Every turn must advance to new beats. Pacing controls beats-per-turn and detail-per-beat, not whether to advance.**

| Pacing | Beats/Turn | Detail/Beat | Encounter Duration |
|--------|-----------|-------------|-------------------|
| Slow | 1 | Rich, deep sensory/emotional | ~8-9 turns |
| Medium | 1-2 | Moderate | ~4-6 turns |
| Fast | Full arc (3+) | Minimal, efficient | ~2-3 turns |
