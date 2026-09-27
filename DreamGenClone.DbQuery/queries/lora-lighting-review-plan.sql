-- Read-only: the LIVE coverage plan's per-cell context axes, so the lighting/background pairing can be reviewed.
-- PayloadJson.coveragePlanJson is a nested JSON string (JsonSerializerDefaults.Web -> camelCase).
WITH plan(Id, Json) AS (
    SELECT d.Id, json_extract(d.PayloadJson, '$.coveragePlanJson')
    FROM CharacterLoraDatasets d
)
SELECT
    plan.Id AS DatasetId,
    json_extract(r.value, '$.key') AS CellKey,
    json_extract(r.value, '$.role') AS Role,
    json_extract(r.value, '$.lightingKey') AS LightingKey,
    json_extract(r.value, '$.backgroundKey') AS BackgroundKey,
    json_extract(r.value, '$.outfitKey') AS OutfitKey,
    json_extract(r.value, '$.wardrobeState') AS Wardrobe,
    json_extract(r.value, '$.poseClass') AS Pose,
    json_extract(r.value, '$.split') AS Split
FROM plan
JOIN json_each(plan.Json, '$.records') AS r
ORDER BY CellKey;
