UPDATE SceneImages
SET Status = 'Cancelled',
    ErrorMessage = 'Cancelled from Production Studio before image generation completed.',
    UpdatedUtc = '2026-09-06T18:07:05.0788851Z',
    CompletedUtc = '2026-09-06T18:07:05.0788851Z'
WHERE Id = 'c95c8082-96a0-4e38-96bd-40ee083d5622'
  AND ProductionGroupId = '3004e8c0-bb83-4c2a-8cb8-d1b7d1e6934b'
  AND Status = 'Pending';