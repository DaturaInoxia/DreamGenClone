---
name: runpod-pod-migration
description: 'Operate DreamGenClone RunPod migrations and Model Manager synchronization. Use when asked to check pod status, update pods, start a pod, handle unavailable GPUs, respond to "pod migrated", find a migrated pod ID, or update a Model Manager RunPod endpoint.'
argument-hint: '<deployment manifest or pod name>'
user-invocable: true
---

# RunPod Pod Migration

## Purpose

Find a RunPod migration successor and synchronize its proxy URL into Model Manager without creating, migrating, terminating, or deleting pods.

## Procedure

1. Read the deployment manifest under `helpers/runpod/deployments/`.
2. List account pods through RunPod GraphQL, including `id`, `name`, `desiredStatus`, `imageName`, `machineId`, and runtime ports.
3. If asked to start the original pod, use the existing start operation. Do not create a pod.
4. If start reports unavailable GPU capacity, tell the user to use RunPod's Migrate action. The public REST API and CLI do not expose the beta migration operation.
5. After the user reports the pod migrated, find exactly one successor that:
   - Is `RUNNING`.
   - Has a different ID from the manifest.
   - Uses the manifest's `containerImage`.
   - Has the manifest `podName` or a name beginning with `<podName>-migration`.
6. Require the manifest HTTP port to be present in runtime mappings.
7. Do not perform readiness, model-presence, or model-identity validation on the migrated pod. RunPod `RUNNING` status is the migration acceptance condition.
8. Derive `https://<migratedPodId>-<inferencePort>.proxy.runpod.net`.
9. Query Model Manager providers with `DreamGenClone.DbQuery/queries/runpod-provider-endpoints.sql`.
10. Refuse to repoint a shared provider when the migrated pod serves only one of its assigned models. Create or split a dedicated provider with explicit user approval.
11. Run `provider-endpoint-update <providerId> <expectedCurrentBaseUrl> <newBaseUrl>`. This compare-and-swap guard must remain intact.
12. Query provider endpoints again and report the old and new pod IDs, provider ID, status, and endpoint change.

## Canonical Command

```powershell
.\helpers\runpod\start-and-sync-provider.ps1 `
  -ManifestPath <MANIFEST> `
  -ProviderId <DEDICATED_PROVIDER_ID> `
  -ExpectedCurrentBaseUrl <CURRENT_BASE_URL> `
  -RuntimeTimeoutSeconds <TIMEOUT> `
  -PollIntervalSeconds <INTERVAL>
```

## Safety Rules

- Never call pod creation, deletion, or termination APIs.
- Never fabricate a pod ID or endpoint.
- Never update Model Manager before the migrated pod is `RUNNING` with its HTTP port exposed.
- Never silently choose among multiple migrated successors.
- Never bypass the endpoint compare-and-swap guard.