-- Where are the head's own COCO points — the BLUE frame the renderer draws (neck-nose, nose-eyes, eyes-ears) — relative
-- to the FACE box that PoseFaceProxy rotates about?
--
-- This decides two things in the proxy:
--   1) whether those points are really the head in a face-framed crop, or the detector artifacts this repo has already
--      measured (a face crop makes DWPose report body joints that are not in the picture at all); and
--   2) whether the face box is wide enough to COVER them. The proxy's depth profile is defined across the head's own
--      width, so a point OUTSIDE that width has a lateral beyond 1 and the profile extrapolates instead of clamping.
--
-- Lateral = |x - faceCentreX| / (faceWidth / 2). <= 1 means the point is inside the face's own width.
WITH p AS (
    SELECT Id, Name, KeypointsJson AS j
    FROM PosePresets
    WHERE Id LIKE '%head%' OR Name LIKE '%head%'
),
f(m) AS (SELECT 0 UNION ALL SELECT m + 1 FROM f WHERE m < 69),
vis AS (
    SELECT p.Id AS Preset,
           f.m * 3 AS o
    FROM p
    JOIN f
    WHERE CAST(json_extract(p.j, '$."face_keypoints_2d"[' || (f.m * 3 + 2) || ']') AS REAL) > 0.1
),
box AS (
    SELECT vis.Preset,
           MIN(CAST(json_extract(p.j, '$."face_keypoints_2d"[' || vis.o || ']') AS REAL)) AS x0,
           MAX(CAST(json_extract(p.j, '$."face_keypoints_2d"[' || vis.o || ']') AS REAL)) AS x1,
           MIN(CAST(json_extract(p.j, '$."face_keypoints_2d"[' || (vis.o + 1) || ']') AS REAL)) AS y0,
           MAX(CAST(json_extract(p.j, '$."face_keypoints_2d"[' || (vis.o + 1) || ']') AS REAL)) AS y1,
           COUNT(*) AS VisibleFace
    FROM vis
    JOIN p ON p.Id = vis.Preset
    GROUP BY vis.Preset
),
idx(n, point) AS (VALUES (0, 'nose'), (1, 'neck'), (14, 'rightEye'), (15, 'leftEye'), (16, 'rightEar'), (17, 'leftEar'))
SELECT p.Name,
       box.VisibleFace,
       ROUND(box.x0, 1) AS FaceX0,
       ROUND(box.x1, 1) AS FaceX1,
       ROUND(box.y0, 1) AS FaceY0,
       ROUND(box.y1, 1) AS FaceY1,
       ROUND(box.x1 - box.x0, 1) AS FaceW,
       idx.point,
       ROUND(CAST(json_extract(p.j, '$."pose_keypoints_2d"[' || (idx.n * 3) || ']') AS REAL), 1) AS X,
       ROUND(CAST(json_extract(p.j, '$."pose_keypoints_2d"[' || (idx.n * 3 + 1) || ']') AS REAL), 1) AS Y,
       ROUND(CAST(json_extract(p.j, '$."pose_keypoints_2d"[' || (idx.n * 3 + 2) || ']') AS REAL), 2) AS Conf,
       CASE
           WHEN (box.x1 - box.x0) <= 0 THEN NULL
           ELSE ROUND(
               ABS(CAST(json_extract(p.j, '$."pose_keypoints_2d"[' || (idx.n * 3) || ']') AS REAL)
                   - ((box.x0 + box.x1) / 2.0)) / ((box.x1 - box.x0) / 2.0), 2)
       END AS Lateral
FROM p
JOIN box ON box.Preset = p.Id, idx
ORDER BY p.Name, idx.n;
