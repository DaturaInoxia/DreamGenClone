UPDATE SceneImages
SET Status = 'Failed',
    ErrorMessage = 'Cancelled before the staged durable Identity job was started.',
    UpdatedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now'),
    CompletedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE Id = '0daa2b02-8d72-4579-b2c9-cade62714644'
  AND Status = 'Pending'
  AND ProductionGroupId = '3004e8c0-bb83-4c2a-8cb8-d1b7d1e6934b';