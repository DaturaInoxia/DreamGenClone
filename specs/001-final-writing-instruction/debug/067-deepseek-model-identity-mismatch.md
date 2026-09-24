# 067 — Beat production `structured_text_model_identity_mismatch` (DeepSeek canonical model name)

- **Session**: `8bc36efb-b235-485b-8675-b98ad7754e59` · Interaction `6946a8a6-092c-4336-87d8-bedcaaa9749a`
- **Error**: `structured_text_model_identity_mismatch` — "The structured text provider returned an unexpected model identity."
- **Failing jobs**: `89eda005-394f-4278-bbda-17d7f073d571`, `3eaa63f8-7e8e-46ee-9086-cf72cd86518d` (Lane `TextAnalysis`, JobType `SceneBeatProductionPlan`)
- **Failing attempts**: `6405c0af-6e22-46a1-ae82-7f8a6fa919a8`, `f08be165-46b2-4edb-988c-7afeca09aa32`
- **Date**: 2026-09-23 21:15–21:16 local (2026-09-24 01:15Z)

## Report

Beat production failed twice on the `structure` pass. Both attempts ran ~25–27 s and produced
`outputCharacters: null`, which means the provider *did* return HTTP 200 with content — the response was
read and parsed, then rejected. Not a transport, timeout, schema, or token-budget failure.

`ValidationDetailsJson` showed `passTrace` failing on `passId: "structure"` with the identity error.

## Analysis

### Code path

`DreamGenClone.Infrastructure/Models/OpenAiStructuredTextCompletionClient.cs:214`:

```csharp
if (!string.Equals(parsed.Model, resolved.ModelIdentifier, StringComparison.Ordinal))
    throw new StructuredTextCompletionException("structured_text_model_identity_mismatch", ...);
```

The response `model` field must be **byte-identical** to the registered `ModelIdentifier`. Introduced with
commit `72e58d4 "Implement progressive scene beat pipeline"`. It is **not** required by any B-100 spec
requirement — a hand-added defensive guard.

Important: it protects nothing today. `SceneBeatAnalyzerExecutionSnapshot.FromResolved` records
`analyzer.Model.ModelIdentifier` (from config), not the echoed value, so auditable provenance does not
depend on this check. A mis-match can only fail the job; it cannot be reported usefully.

### Configuration in play

`RolePlaySceneBeatAnalyzer` → provider `DeepSeek` (`https://api.deepseek.com`, Type 2), model row
`f4eeb102-792c-4a5c-99bc-68906afea167`, `StructuredOutputMode=JsonObject`, `ThinkingMode=Disabled`,
`MaxTokens=64000`.

### Live provider evidence

`GET https://api.deepseek.com/v1/models` returns exactly:

```
id='deepseek-flash'    owned_by='deepseek'
id='deepseek-v4-pro'   owned_by='deepseek'
```

Request probe (JSON-object mode, same shape the client sends):

| Requested `model` | HTTP | Provider echoed `model` | Exact match |
|---|---|---|---|
| `deepseek-v4-flash` (**was configured**) | 200 | `deepseek-flash` | **False** |
| `deepseek-flash` | 200 | `deepseek-flash` | True |
| `deepseek-v4-pro` | 200 | `deepseek-v4-pro` | True |
| `deepseek-v4.1-flash` | 400 | — | name does not exist |
| `deepseek-v4-flash-4.1` | 400 | — | name does not exist |
| `deepseek-chat` | 200 | `deepseek-flash` | False |

**Root cause**: `deepseek-v4-flash` is accepted by DeepSeek as a **request alias**, but the provider always
reports the canonical `deepseek-flash`. The strict `Ordinal` equality check can therefore *never* pass for
that row — a guaranteed 100 % failure on every call, not an intermittent one. DeepSeek appears to have
renamed the model after 2026-09-08 (debug `044` recorded the same row completing successfully).

### Why only beat production broke

Only two clients enforce the check: `OpenAiStructuredTextCompletionClient` and
`OpenAiMultimodalCompletionClient`. The streaming `CompletionClient` (RP generation) has no such check, so
the stale alias was silently tolerated everywhere except the strict structured-text paths.

### Prior art

`specs/001-rp-prompt-redesign/debug/062-b122-body-card-prefill.md` already recorded this exact failure for
the B-122 body-card draft function and predicted it "would affect the beat analyzer too". That function was
worked around by moving to `OP-glm-4.7`; the analyzer stayed on the stale alias.

### Blast radius

Row `f4eeb102-…` is shared by 10 functions in the dev DB (8 in the snapshot): `RolePlaySemanticAnalysis`,
`RolePlaySummaryEnhancement`, `RolePlayLocationDetection`, `RolePlayActorSelection`, `RolePlayGeneration`,
`RolePlaySteering`, `RolePlayEncounterDetection`, `RolePlaySceneImagePreprocessor`,
`RolePlaySceneBeatAnalyzer`, `RolePlayCharacterBodyCardDraft`.

## Plan

Data-only convergence, via the existing sanctioned provisioning command (there is no supported
snapshot-refresh command, so the snapshot is deliberately NOT copied over `dev.db`):

1. `DreamGenClone.DbQuery/Program.cs` — `ConfigureB100AnalyzerAsync` converges `ModelIdentifier` to
   `deepseek-flash`, accepting the legacy `deepseek-v4-flash` alias as input (idempotent).
2. Run `helpers/dbq.ps1 b100-analyzer-configure` against the dev DB.
3. `DreamGenClone.Web/data/model-manager.export.json` — fix the importable portable config.
4. `docs/db-snapshot-setup.md`, `docs/setup-other-machine.md` — update the documented pair name.

## Resolution

| File | Change |
|---|---|
| `DreamGenClone.DbQuery/Program.cs` | `modelIdentifier` → `deepseek-flash`; added `legacyModelIdentifier`; select matches either; `configureModel` now sets `ModelIdentifier` too |
| `DreamGenClone.Web/data/dreamgenclone.dev.db` | `RegisteredModels.ModelIdentifier` for row `f4eeb102-…`: `deepseek-v4-flash` → `deepseek-flash` |
| `DreamGenClone.Web/data/model-manager.export.json` | `ModelIdentifier` → `deepseek-flash` for that row |
| `docs/db-snapshot-setup.md`, `docs/setup-other-machine.md` | documented pair name + alias-convergence note |

Verified DB diff against a pre-change backup: **one field changed**, `ModelIdentifier` on the one row;
`Providers` unchanged, and the `RolePlaySceneBeatAnalyzer` function-default row is byte-identical.

**Side effect found and corrected**: `b100-analyzer-configure` upserts the *whole* function-default row, so
running it reset `MaxTokens` 64000 → 4000. Restored to 64000. Any future re-run of that command will reset
it again — this is a known sharp edge of the provisioning command, not a code defect.

**No app restart required**: the model-manager repositories have no caching layer, so the row is read fresh
on each resolution.

Tests: solution build 0 errors; `~StructuredText|~SceneBeatAnalyzer|~ModelManager` 31/31 green;
`~SceneBeat|~SceneMoment` 136/136 green.

## Validated

- [x] **2026-09-23 21:45–21:47 local** — beat production plans produced after the fix are `Complete` with
      `ModelIdentifier=deepseek-flash` (1 sound cue, 7–23 dialogue cues). The earlier 01:15–01:16 attempts
      remain `Superseded` with `ModelIdentifier=deepseek-v4-flash`. The `structure` pass no longer fails.
- [ ] Follow-up opened: the pipeline now advances past beat production and fails at **Moment enrichment**
      (`scene_moment_enrichment_output_invalid`, job `9fa8e710-…`). Separate defect — the image tier removed
      the soundscape pass but moment discovery still mandates the `SoundEventAnchor` role. See debug `068`.
- [ ] Optional follow-up not taken: include the echoed model identity in the exception message, so this
      class of failure is diagnosable without a live provider probe.
- [ ] Optional follow-up not taken: the row's `DisplayName` remains `deepseek-v4-flash` (the identifier no
      longer matches it). Cosmetic only.
