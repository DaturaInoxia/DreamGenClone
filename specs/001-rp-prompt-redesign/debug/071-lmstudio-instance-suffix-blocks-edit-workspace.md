# 071 — "Prepare edit" disabled: an LM Studio instance-suffix broke the multimodal identity check

**Status:** Diagnosed (no code change yet); provider-side unblock available now
**Date:** 2026-09-24
**Report:** "why is the prepare edit not enabled now" — Character Studio → Faces → Panel C → three-quarter view → the edit workspace's **Prepare edit** button greyed out, with the intent box filled in.

## The chain, with evidence

1. **The button's own rule.** `EditIterateWorkbench` disables it on
   `Busy || HasInFlightWork || Intent empty || ReadOnly`. The intent box had text and was editable, so the cause is
   **work in flight**: `HasInFlightWork()` = `_descriptionPending || _attempt?.IsInFlight || _result?.IsInFlight`.
2. **What is in flight.** The workspace queues a *source description* on load
   (`ImageEditWorkspace.LoadAsync` → `ReanalyzeAsync` → `_descriptionPending = true`) and clears the flag **only when
   `session.DescriptionText` becomes non-empty**. On the sessions the panel opened
   (`599aeb35…`, `663325a5…`, `46c3f9c8…`, `cd82db64…`, `b761f7da…` — all on the canonical front `3b8f7dd9…`,
   created 01:50:58–01:53:34) the description text is **empty**: the description jobs **failed**. So the flag never
   clears, `HasInFlightWork()` stays true forever, `Prepare edit` stays disabled, and the poll loop never terminates.
3. **Why the description jobs failed.** `DurableBackgroundJobs` shows `scene-asset-image-edit-description`
   **Complete up to 01:39:03**, then **Failed from 01:50:58** (26 failures in total; three on these sessions):
   - first failure: `Multimodal completion failed for provider 'Local LM Studio (WOODGame 4060 Ti)' with HTTP 400.`
   - every later one: `The multimodal provider returned an unexpected model identity.`
   Meanwhile the readiness probe two seconds before each failure logs
   `Multimodal readiness check succeeded: Model=qwen2.5-vl-7b-instruct-abliterated-local`.
4. **Why the identity check trips.** Probed directly (`http://192.168.0.192:1234`, the provider the registered
   `qwen2.5-vl-7b-instruct-abliterated-local` row points at):

   ```
   POST /v1/chat/completions  {"model":"qwen2.5-vl-7b-instruct-abliterated-local", …}
        → response.model = "qwen2.5-vl-7b-instruct-abliterated-local:2"
   ```

   LM Studio's own REST API shows why (one instance is loaded — the `:2` is not a second load):

   ```
   GET /api/v0/models  →  qwen2.5-vl-7b-instruct-abliterated-local:2   vlm  qwen2vl  Q4_K_M  loaded
                          qwen2.5-vl-7b-instruct-abliterated-local     vlm  qwen2vl  Q4_K_M  not-loaded
                          qwen2.5-vl-7b-instruct-abliterated              vlm  qwen2vl  Q4_K_M  not-loaded
   ```

   The models folder holds **two copies of the same model key** (`…-abliterated-local`), so LM Studio disambiguates
   the second one as `…-local:2` — and the copy the operator loaded is that one. The app asks for the plain id, the
   served instance echoes the suffixed id, and `OpenAiMultimodalCompletionClient.GenerateAsync` requires
   `parsed.Model == model.ModelIdentifier` **exactly**, so it refuses rather than editing with a model nobody asked
   for.

   The readiness probe passes anyway because LM Studio's `/v1/models` lists the **whole library** (16 ids, one of
   them `loaded`), so the contract `{"object":"list","data":[{"id":"…-local"}]}` matches a model that is *not*
   the one being served. The completion's identity check is the only thing that caught it.

Nothing in the application changed at 01:50 — the provider's behaviour did.

## Resolution (code)

| Change | Where |
|---|---|
| `GetDescriptionOutcomeAsync(sessionId)` — the description job's outcome, read from its deterministic row `<jobType>:<sessionId>`; null when there is no such row | `IImageEditWorkspaceService` + both adapters (`SceneAssetImageEditWorkspaceService`, `SceneImageEditWorkspaceService`, each now taking `IDurableBackgroundJobRepository`) |
| `ImageEditDescriptionOutcome.From(job)` — terminal = `Complete`/`Failed`/`Cancelled`; a failure carries the job's message (or its error code) | same file as the interface |
| A terminal description ends the wait: `_descriptionPending` clears, the failure is shown as the panel's error, the poll stops, and the editor becomes usable — the description is compiler context, not a prerequisite for preparing an edit | `ImageEditWorkspace.razor` `RefreshAsync` |
| The identity errors name **expected vs actual** | `OpenAiMultimodalCompletionClient` (multimodal) and `OpenAiStructuredTextCompletionClient` (structured text) |

Tests: `ImageEditDescriptionOutcomeTests` (terminal/in-flight mapping, error-code fallback) and
`ImageEditWorkspaceContractTests.AFailedSourceDescription_IsReported_InsteadOfWaitingForever` (the component reads
the outcome, both adapters look up their own job type).

The identity check itself stays **strict**, matching the precedent in `001-final-writing-instruction/debug/067`
(the DeepSeek alias was converged in data, not tolerated in code). The trigger here was a duplicated model key in the
LM Studio library; the operator ejected and reloaded, the plain id is served again, and the edit loop works. The
diagnostic now names both ids, so the next occurrence is a one-line read instead of a database dig.

**Verification status at the time of writing:** `DreamGenClone.Web` builds with **0 errors** (Razor + Infrastructure
included). The test project could not be built to run the new tests: `PoseAuthoringTests.cs` (an untracked file from
the concurrent pose-library work) was mid-edit by another session — the compile errors changed between two
consecutive builds. Re-run `~ImageEditWorkspace|~ImageEditDescriptionOutcome` once that file compiles.

## Provider decision (for the record)

`ModelIdentifier` strictness, readiness probing and the duplicate library key are **configuration**, not app
behaviour. Two supported fixes, operator's choice: load the copy whose id has no suffix (and/or delete the duplicate
from the models folder so the key is unique), or re-register the model identifier to the id the provider serves.
Chosen: the former (no app change), with the code above making a recurrence cheap to diagnose and impossible to
turn into a dead panel.

## The two defects this uncovered

1. **Provider/config (the trigger).** A duplicated model key in the LM Studio library, whose loaded copy carries the
   `:2` suffix. Unblock, either: load the copy whose identifier has **no** suffix (and/or delete the duplicate from the
   models folder so the key is unique again), or re-register the app's model identifier to the id the provider
   actually serves (`…-local:2`) — the first is preferred, because a hard-coded instance suffix breaks as soon as the
   duplicate is cleaned up. Secondary finding: the readiness contract cannot see this, because LM Studio's
   `/v1/models` lists the library rather than the loaded instance; a readiness check that proves the **serving**
   identity (LM Studio's `/api/v0/models` `state=loaded` entry, or a completion-identity echo) would catch it.
2. **Component failure path (the dead end).** A panel must not become unusable, with no message, because a *context*
   job failed. `_descriptionPending` had no failure state: the workspace neither read the durable job's terminal
   status nor bounded the wait, and the description is not required to compile an edit (`PrepareAsync` needs the
   intent only). Resolved above.

## Small diagnostic gap — now closed

`OpenAiMultimodalCompletionClient` reported "unexpected model identity" without naming **expected vs actual**, which
is why this took a database dig plus a direct probe to pin down. Both completion clients now name both ids.

## Open follow-up

The ↻ **Re-analyze source** button re-enqueues the description job under the same deterministic id
(`<jobType>:<sessionId>`), which `TryEnqueueAsync` refuses because that row already exists — so a session whose
description job is terminal cannot re-run its analysis in place; a freshly opened session (a page reload) does.
Worth either a fresh job id per re-analysis attempt or a clear message at that call site.

1. **Provider/config (the trigger).** A duplicated model key in the LM Studio library, whose loaded copy carries the
   `:2` suffix. Unblock, either: load the copy whose identifier has **no** suffix (and/or delete the duplicate from the
   models folder so the key is unique again), or re-register the app's model identifier to the id the provider
   actually serves (`…-local:2`) — the first is preferred, because a hard-coded instance suffix breaks as soon as the
   duplicate is cleaned up. Secondary finding: the readiness contract cannot see this, because LM Studio's
   `/v1/models` lists the library rather than the loaded instance; a readiness check that proves the **serving**
   identity (LM Studio's `/api/v0/models` `state=loaded` entry, or a completion-identity echo) would catch it.
2. **Component failure path (the dead end).** A panel must not become unusable, with no message, because a *context*
   job failed. `_descriptionPending` has no failure state: the workspace neither reads the durable job's terminal
   status nor bounds the wait, and the description is not required to compile an edit (`PrepareAsync` needs the intent
   only). The honest behaviour: when the description job for the session is terminal and the text is still empty,
   clear the flag, stop the poll, and show the job's own failure text where the description would be.

## Small diagnostic gap worth closing

`OpenAiMultimodalCompletionClient` reports "unexpected model identity" without naming **expected vs actual**, which
is why this took a database dig plus a direct probe to pin down. Naming both values turns it into a one-line answer.
