UPDATE Providers
SET ImageCapability = 1,
    ImageGenerationPath = '/v1/images/generations',
    ImageProtocol = 0,
    UpdatedUtc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
WHERE Id = '86e7c25e-c736-4c99-b418-f3512689546a'
  AND Name = 'OpenRouter';