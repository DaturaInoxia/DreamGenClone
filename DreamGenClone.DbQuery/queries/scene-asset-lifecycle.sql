SELECT Id, Name, Kind, Status, Type, CreatedUtc, StartedUtc, CompletedUtc, UpdatedUtc,
       ErrorMessage, AssociationMetadataJson, ProductionApprovalStatus
FROM SceneAssets
WHERE Id = '{{id}}';
