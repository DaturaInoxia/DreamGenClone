# T153 Legacy One-Shot Removal Audit

**Audit date:** 2026-08-31
**Decision:** Blocked; leave T153 unchecked.

## Live Data Evidence

The read-only development database audit reported:

| Metric | Count |
|---|---:|
| `SceneImageBeatAnalyses` total | 14 |
| Complete | 13 |
| Rows retaining raw response or reasoning | 14 |
| Active durable `scene-image-beat-generation` jobs | 0 |

Historical rows remain readable and untouched. Zero active jobs proves there is no current queue
drain requirement; it does not prove the execution path is removable.

## Active Code Dependencies

- `SceneImageStudio.GenerateBeatsAsync` still exposes the explicitly labeled temporary schema-v3 preparation command.
- `SceneImageService.EnqueuePromptAsync` calls `GetBeatAnalysisByTurnAsync`, validates the selected legacy analysis and schema-v3 beat, and writes `SceneImagePromptRecord.BeatAnalysisId` plus `BeatSnapshotJson`.
- `SceneImagePromptGenerationJobHandler` deserializes `SceneImageBeat` from the prompt record before compiler dispatch.
- `SceneImageRenderBriefBuilder` and `SceneImageRenderingJobHandler` validate or reconstruct schema-v3 `SceneImageBeat` data.
- `PonySceneImagePromptBuilder` retains the schema-v3 beat contract for current prompt compilation.
- `SceneImageRepository`, `ISceneImageRepository`, and `ISceneImageService` retain historical schema-v3 reads.

The primary **Generate Beats** command is already canonical and calls
`SceneBeatPipelineService.EnqueueCatalogueAsync`. The legacy command is now named **Prepare Legacy
Prompt Input** and is deliberately separate.

## Removal Prerequisites

1. A current B-100 Still `CompiledMediaBrief` must feed prompt generation and image execution without a `SceneImageBeatAnalysisRecord` or schema-v3 `SceneImageBeat` snapshot.
2. Composition attempts must persist and execute from the exact current Catalogue, Beat Production Plan, Moment Set, Moment Enrichment, and compiled-brief lineage.
3. Pony and SDXL compiler/execution tests must prove no schema-v3 beat input is read.
4. Existing prompt and image records must remain reproducible/readable through a historical compatibility reader.
5. A fresh source/reference audit and the read-only database audit below must show no active execution dependency.

There is no arbitrary waiting period. Removal is gated by these executable prerequisites and the
reference audit.

## Exact Removal Candidates After Unblock

- `SceneImageStudio.GenerateBeatsAsync` and its temporary legacy preparation controls.
- `SceneImageService.EnqueueBeatAnalysisAsync` and active prompt-generation validation against the legacy analysis.
- `SceneImageBeatGenerationJobHandler` registration and `BackgroundJobTypes.SceneImageBeatGeneration` enqueue path.
- `SceneImageBeatAnalysisService` as an active generator/parser.
- Active schema-v3 branches in `SceneImagePromptGenerationJobHandler`, `SceneImageRenderBriefBuilder`, `SceneImageRenderingJobHandler`, and image prompt builders.

Do not delete `SceneImageBeatAnalyses`, its historical repository read API, or old record fields until
the compatibility reader and retention requirements have their own approved migration.

## Re-Audit

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b100-legacy-beat-audit.sql
```

The companion model metadata inventory remains:

```powershell
powershell -ExecutionPolicy RemoteSigned -File helpers/dbq.ps1 sql DreamGenClone.DbQuery/queries/b100-image-model-family-inventory.sql
```