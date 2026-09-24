-- Every scene-image row touched by the post-066 restart (03:06Z onward), plus the specific ids the log named.
SELECT Id, BeatId, ProductionStage, Operation, Status, CreatedUtc, StartedUtc, CompletedUtc, FileRelativePath
FROM SceneImages
WHERE Id IN (
        'c49b4875-8839-43c6-a2ca-ee5b46633839',
        'cc53951c-2411-460a-81bd-33fc18c1e2c8',
        'd1140ce1-56cc-4e38-b807-36450682b9d5',
        'ee16eb2c-331f-41ab-b03c-01e99fe59f55',
        'ce808667-df02-459b-b3a9-01a458281873',
        '99a3cf6f-6180-4881-b27b-013be081e8a9',
        'eea7cfd7-b820-4f97-b36b-ace587b10cb0',
        '50724d19-6028-4770-adcd-d50494ea1c3c')
   OR (Operation = 'Edit' AND CreatedUtc >= '2026-09-22T03:00:00Z')
ORDER BY CreatedUtc DESC;
