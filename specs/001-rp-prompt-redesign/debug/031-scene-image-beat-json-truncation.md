# 031 — Scene image beat analysis JSON truncated by reasoning-model token ceiling

## Report

- Studio route: `/roleplay/studio/423a270c-49a6-4cad-9ccf-94d68bdc6e7c/3c50e37b-8eca-4506-b408-c0e3105c4eba`
- Session: `423a270c-49a6-4cad-9ccf-94d68bdc6e7c`
- Anchor interaction: `3c50e37b-8eca-4506-b408-c0e3105c4eba`
- Turn: `433a8524582f43a0a73eb4e911471c48`
- Beat analysis record: `d0849727-9277-4a39-ba28-6b23ba46cdb0`
- Error shown in Studio: `Beat analysis failed: Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 | BytePositionInLine: 10702.`
- Occurred: 2026-08-23 15:06–15:08 -04:00.

## Analysis

The error is a `System.Text.Json` depth error thrown by `SceneImageBeatAnalysisService.ParseOutput` (line 84) — the model returned a JSON payload that was cut off mid-string, so the root object/beats array were never closed.

Persisted evidence (`SceneImageBeatAnalyses` row `d0849727...`):
- `RawModelResponse` = 10,766 chars of JSON that begins `{"beats":[{"schemaVersion":3,"beatId":"b1",...` and ends abruptly at `..."profileId":"f58f959a-8050-4388-a219-99d2df3446` (missing closing quote and `]}`). The last `}` in the output is at byte 10701, which is why `JsonDocument.Parse` reported `BytePositionInLine: 10702`.
- `ReasoningContent` = 36,375 chars.

Application log chain (`DreamGenClone.Web/logs/dreamgenclone-20260823.log`):
1. `RolePlaySceneImagePreprocessor` resolved to `deepseek-v4-flash` (Provider=DeepSeek) with **MaxTokens=8000**, ThinkingMode=Default.
2. First call produced 36,375 reasoning chars → consumed the full 8K-token budget → `content=0`, `finish_reason=length`.
3. `SendCompletionWithReasoningAsync` (reasoning-aware) detected empty-content + length and called `ForceAnswerFromReasoningAsync`.
4. The force-answer call re-reasoned (20,886 chars) despite "output ONLY the final answer", then emitted the beat JSON but hit the ceiling → returned 10,766 chars of truncated JSON, `finish_reason=length`.
5. `ForceAnswerFromReasoningAsync` only handled the empty-content case; it returned the truncated content as-is.
6. `SceneImageBeatAnalysisService.ParseOutput` strict JSON parse failed; the handler marked the record Failed and surfaced the message.

Root cause: `RolePlaySceneImagePreprocessor`'s `deepseek-v4-flash` function default had `MaxTokens=8000`. DeepSeek V4-Flash reasons by default (even with ThinkingMode=Default) and consistently spends 20–37K chars on `reasoning_content` before emitting content, leaving too little output budget to finish the beat JSON. This is the **identical failure mode previously fixed for `RolePlaySemanticAnalysis`** (also `deepseek-v4-flash`) by raising MaxTokens 8000→16000 (backlog B-081); that function now works and `RolePlaySceneImagePreprocessor` had never been raised.

Secondary gap: `ForceAnswerFromReasoningAsync` did not attempt `ContinueTruncatedResponseAsync` when its result was non-empty but hit `finish_reason=length`, unlike the main reasoning-aware path which does.

## Plan (approved)

1. **Config (no code):** Raise `RolePlaySceneImagePreprocessor` `MaxTokens` 8000 → 16000 in `FunctionModelDefaults` (matches the proven B-081 fix; UI cap already supports 16000).
2. **Code hardening (safety net):** In `CompletionClient.ForceAnswerFromReasoningAsync`, when the force-answer returns non-empty content with `finish_reason=length`, run `ContinueTruncatedResponseAsync` to give the remaining answer a chance to complete — mirroring the main reasoning-aware path. Keep strict parser behavior; no JSON repair, no invented content.
3. Add client-level regression tests for the truncated-force-answer continuation.
4. Create this debug record and validate.

## Resolution

- **Config:** `FunctionModelDefaults` row for `RolePlaySceneImagePreprocessor` updated `MaxTokens` 8000 → 16000 in `DreamGenClone.Web/data/dreamgenclone.dev.db`. A backup row copy was written to `_backup_FunctionModelDefaults_SceneImagePreprocessor` before the update (restore via `INSERT INTO FunctionModelDefaults SELECT * FROM _backup_FunctionModelDefaults_SceneImagePreprocessor` if ever needed).
- **Code:** `DreamGenClone.Infrastructure/Models/CompletionClient.cs` — `ForceAnswerFromReasoningAsync` now, when its result is non-empty but `finish_reason=length`, calls `ContinueTruncatedResponseAsync` and adopts the continuation result when it extends the content. The stale comment on the empty-content branch was corrected (it returns empty to fail explicitly; it does not append reasoning). Blast radius: `CompletionClient.cs` + completion tests only; no prompt-slot, gate, or parser change.
- **Tests:** `CompletionClientReasoningTests` — added `GenerateWithReasoningAsync_TruncatedForceAnswer_AppendsContinuationToCloseJson` and `GenerateWithReasoningAsync_TruncatedForceAnswer_NoContinuationKeepsTruncatedContent` (3 total completion-reasoning tests).

## Validated

- [x] Full test suite: 1224 passed, 0 failed (Debug).
- [x] CompletionClientReasoningTests: 3 passed, 0 failed.
- [x] Scene Image tests: 111 passed, 0 failed.
- [x] No editor diagnostics in touched implementation/test files.
- [x] Fresh Scene Image Studio run on session `423a270c...` (2026-08-23): beat generation **completed** — record `c674427c-7fd7-486d-934a-37e1866cb3a8` Status=Complete, RawModelResponse=10,724 chars (complete JSON, no truncation), ReasoningContent=10,779, BeatsJson=12,587. Prompt projection + ComfyUI rendering produced images for the turn.

### Follow-up observation: provider timeout with the raised token ceiling

- The first regeneration attempt (16:23:20) timed out after **120s** (`HttpClient.Timeout ... elapsing`) — with MaxTokens=16000, `deepseek-v4-flash` occasionally reasons past the old provider timeout before emitting content.
- User raised the DeepSeek provider `TimeoutSeconds` **120 → 240** via Model Manager (saved 16:29:49, `Providers` row `cf9b358b-...`). The retry (16:29:55) succeeded in ~31s.
- This is a UI-backed provider setting; no code change required. If slow reasoning recurs, the timeout is already generous at 240s.
