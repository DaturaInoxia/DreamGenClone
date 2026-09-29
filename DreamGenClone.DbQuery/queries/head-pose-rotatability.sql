-- Why can't these two authored head poses be rotated the way a body pose is?
--
-- The answer is how many BODY joints are actually visible in them, because the rig fit needs a body to fit onto — and
-- the editor's guard refuses any pose carrying a face channel WITHOUT EVER CHECKING whether it also has a body.
-- A face-framed crop is known to make DWPose report body joints that are not in the picture at all, so the count is
-- the fact that settles this, not the presence of the face channel.
WITH p AS (
    SELECT Id, Name, KeypointsJson
    FROM PosePresets
    WHERE Id IN (
        'authored-34-right-head-ae24d6e3',
        'authored-extracted-77a0862a82f04c9dbb9bd2c5b36bb35b-86823143'
    )
),
b(n) AS (SELECT 0 UNION ALL SELECT n + 1 FROM b WHERE n < 17),
f(m) AS (SELECT 0 UNION ALL SELECT m + 1 FROM f WHERE m < 69)
SELECT p.Id,
       (SELECT COUNT(*) FROM b WHERE CAST(json_extract(p.KeypointsJson,
            '$."pose_keypoints_2d"[' || (b.n * 3 + 2) || ']') AS REAL) > 0.1) AS VisibleBody,
       json_array_length(p.KeypointsJson, '$."face_keypoints_2d"') / 3 AS FacePoints,
       (SELECT COUNT(*) FROM f WHERE CAST(json_extract(p.KeypointsJson,
            '$."face_keypoints_2d"[' || (f.m * 3 + 2) || ']') AS REAL) > 0.1) AS VisibleFace,
       (SELECT CAST(MIN(json_extract(p.KeypointsJson, '$."pose_keypoints_2d"[' || (b.n * 3) || ']')) AS REAL) FROM b)
           AS BodyXMin,
       (SELECT CAST(MAX(json_extract(p.KeypointsJson, '$."pose_keypoints_2d"[' || (b.n * 3) || ']')) AS REAL) FROM b)
           AS BodyXMax,
       (SELECT CAST(MAX(json_extract(p.KeypointsJson, '$."pose_keypoints_2d"[' || (b.n * 3 + 1) || ']')) AS REAL) FROM b)
           AS BodyYMax
FROM p;
