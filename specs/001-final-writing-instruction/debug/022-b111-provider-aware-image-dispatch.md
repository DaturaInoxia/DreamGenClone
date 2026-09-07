# Debug 022: B-111 Provider-Aware Image Dispatch

## Report

On 2026-09-06, the user corrected the reported Studio queue behavior: self-built serverless creations and edits must be queued to avoid repeated cold starts, while hosted API work such as TogetherAI must execute immediately.

## Analysis

- `SceneImageService.EnqueueRenderAsync` persisted every creation and always added `SceneImageRendering` to the generic background queue.
- `EnqueueEditAsync`, `EnqueueIdentityAsync`, and `EnqueueFinishAsync` likewise always queued `SceneImageEditing`.
- `ProviderExecutionPolicy` classifies `OpenAiImages` as `HostedApi`, `ComfyUi` as `DedicatedPod`, and `ComfyUiServerless` as `SelfBuiltServerless`; B-111 P1 requires serverless drain behavior only for the latter.
- The existing rendering and editing handlers already own image status transitions, provider calls, storage, audit records, and failures. A second execution implementation would create divergent behavior.

## Plan

1. Resolve the exact Model Manager model before dispatching each render or edit.
2. Queue only `ComfyUiServerless` work; invoke the existing handlers immediately for `OpenAiImages` and `ComfyUi`.
3. Fail explicitly for unknown protocols or absent dispatch dependencies.
4. Make Studio action status distinguish immediate completion from serverless queueing.

## Resolution

- Registered concrete rendering and editing handlers while retaining their `IBackgroundJobHandler` registrations for queued execution.
- Added provider-aware dispatch in `SceneImageService` for creations, edits, Identity, and Finish. `ComfyUiServerless` persists to the existing queue; `OpenAiImages` and `ComfyUi` reuse the existing handlers synchronously.
- Updated Compose, Identity, and Finish status messages to state the actual path.

## Validated

- [ ] Pending user validation with a TogetherAI creation and a serverless creation or edit.