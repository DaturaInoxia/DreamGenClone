-- One image by id, whatever batch/asset it belongs to: used to check a view's OutputArtifactId actually exists.
-- {{id}} is the imageId.
SELECT i.Id, i.AssetId, i.CandidateBatchId, i.Kind, i.Status, i.CandidateDecision, i.CreatedUtc,
       i.FileRelativePath, i.ErrorMessage, substr(coalesce(i.Prompt, ''), 1, 80) AS PromptStart
FROM SceneAssetImages i
WHERE i.Id = '{{id}}';
