# 020 — Semantic Inference PARSE-FAILED (Reasoning Model Runaway)

## Report

Session `42b79db3` logged **13 `SemanticInference PARSE-FAILED` errors** across 5 interactions (`583779e2`×4, `f7606c17`×4, `0d1b25bf`×2, `3d597775`×2, `183f0bbe`×1). The failing async 14-event analysis calls returned **30K+ char chain-of-thought reasoning traces with no final JSON**.

Example (interaction `0d1b25bf`):
```
"We need answer JSON. Need analyze interaction for allowed event IDs. Need extract events.
 Need only IDs allowed. Need confidence. Need actorName Dean. ... Let's examine each..."
```

No `{"events":[...]}` envelope was ever emitted → `ExtractJsonObject` found nothing → `InvalidOperationException` → `PARSE-FAILED`. Affected interactions got no stat/theme deltas and no `active-in-encounter` confirmation.

## Analysis

Root cause: `deepseek-v4-flash` is a reasoning model. `SemanticEventInferenceService` uses the plain `GenerateAsync` (content-only) path. When the 14-event prompt triggers extended deliberation, the model either:
1. emits only `reasoning_content` with empty `content` (which `ParseContent` falls back to), or
2. hits `max_tokens` (8000 for this function) mid-reasoning before emitting the final JSON.

Failing calls took 75–128s vs 3–6s for successful ones. The 1-event boundary calls consistently succeeded — the task is small enough to avoid runaway reasoning.

Evidence that reasoning is unnecessary: the task is deterministic JSON event extraction; the system prompt already says "Output ONLY strict JSON." Extended reasoning violates that intent and breaks parsing.

## Plan

Add a scoped "disable thinking" flag that only affects `RolePlaySemanticAnalysis`:
- `ResolvedModel.DisableThinking` (init property, non-positional — no call-site breakage)
- `ModelResolutionService.ResolveAsync` sets it `true` when `function == RolePlaySemanticAnalysis` (both resolve paths)
- `CompletionClient.SendCompletionAsync` adds `chat_template_kwargs: {"thinking": false}` to the payload when the flag is set (semantic analysis uses this path)

No interface changes → no test fakes affected. All other functions unchanged.

**Files:**
- `DreamGenClone.Domain/ModelManager/ResolvedModel.cs`
- `DreamGenClone.Web/Application/ModelManager/ModelResolutionService.cs`
- `DreamGenClone.Infrastructure/Models/CompletionClient.cs`

## Resolution

Implemented all three changes. Added `ChatTemplateKwargs` to the private `ChatRequest` with `JsonIgnore(WhenWritingNull)`. Only `SendCompletionAsync` sets it (the path semantic analysis uses).

**Note:** The request parameter `chat_template_kwargs: {"thinking": false}` is the DeepSeek chat-template standard. Verify against the actual DeepSeek endpoint — if it uses a different suppression param (e.g., `thinking: {"type":"disabled"}` or `enable_thinking`), update the single construction site in `SendCompletionAsync`.

## Validated

- [x] Build: `dotnet build DreamGenClone.Web --no-restore` — 0 errors
- [x] Tests: `SemanticEventInference` + `ModelManager` tests pass
- [ ] Verified live: pending — needs a new session to confirm no more PARSE-FAILED
