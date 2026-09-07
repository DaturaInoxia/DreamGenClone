# Scene Image Editor Multimodal Credential Resolution

## Report

On 2026-09-06, opening the scene-image editor failed with:

`Failed to open the image editor: Function 'RolePlaySceneImageEditPromptCompiler' requires configured inference credential.`

The user reported that the configured `Qwen2.5-VL 7B abliterated image compiler` passed its Model Manager health check and questioned whether the wrong image compiler was being used.

## Analysis

The live development database resolves `RolePlaySceneImageEditPromptCompiler` to the correct enabled Qwen VL model (`db602892-d604-40b1-8f7d-7d6073f7fe1d`) and its enabled Local provider (`e3f20d83-a563-424c-b111-592adfa36e93`) at `https://qwen.kenacwood.net`.

The provider has the explicit credential reference `lmstudio-local`, but `ApiKeyEncrypted` was empty. The readiness health probe does not require an authorization header, while the multimodal compiler resolver and completion client correctly require an explicit inference credential before dispatching a source image.

Model Manager already permits a blank persisted API key when `ModelManagerSecrets` contains the provider's credential reference. However, `ModelResolutionService.ResolveAsync(AppFunction)` only hydrated local secrets for serverless image providers. It did not hydrate the named secret for a local multimodal compiler. The editor's automatic description enqueue therefore failed before the configured Qwen VL compiler could run.

The local Qwen endpoint was verified to return HTTP 200 for `/v1/models` both without authorization and with a harmless Bearer marker. This confirms that `local-qwen-vl` is an explicit local configuration marker rather than an endpoint secret.

Relevant specifications consulted: `specs/001-final-writing-instruction/spec.md` and `specs/001-rp-prompt-redesign/spec.md`.

## Plan

1. Resolve only the exact configured provider credential reference when the stored multimodal credential is absent.
2. Encrypt the resolved local value for the existing completion-client contract, then retain fail-fast behavior when no exact configured value exists.
3. Add an explicit local `lmstudio-local` marker for the no-auth Qwen endpoint.
4. Add focused test coverage ensuring provider-name and global credential keys are not used as substitutes.

## Resolution

- `ModelResolutionService.cs`: `ResolveAsync(AppFunction)` now looks up only `provider.CredentialReference` when `ApiKeyEncrypted` is absent, encrypts a non-empty resolved local value, then enforces the existing inference-credential requirement.
- `SceneImageResolutionTests.cs`: added contract coverage proving that a local multimodal resolution uses the configured credential-reference value and ignores provider-name/default secret values.
- `DreamGenClone.Web/appsettings.Local.json`: added the machine-local, git-ignored `ModelManagerSecrets.lmstudio-local` value `local-qwen-vl`.
- `DreamGenClone.DbQuery/queries/scene-image-edit-compiler-config.sql`: added a read-only configuration diagnostic query.
- Restarted the web application from `DreamGenClone.Web` in Development mode on `http://localhost:5177`.

## Validated

- [x] Live dev DB query confirmed the exact configured Qwen model/provider and identified the missing encrypted credential.
- [x] Local Qwen `/v1/models` accepted both no authorization and `Authorization: Bearer local-health-probe` with HTTP 200.
- [x] `dotnet test DreamGenClone.Tests/DreamGenClone.Tests.csproj --no-restore --nologo -c Release --filter "FullyQualifiedName~SceneImageResolutionTests"` passed: 25 total, 25 passed, 0 failed, 0 skipped.
- [x] Touched C# diagnostics reported no errors.
- [x] Rebuilt app started at `http://localhost:5177` with `ASPNETCORE_ENVIRONMENT=Development` and `Data Source=data/dreamgenclone.dev.db`.
- [ ] Pending user confirmation by opening the scene-image editor and observing the source-description job complete.