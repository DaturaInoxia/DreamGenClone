SELECT
    child.Id AS ChildImageId,
    child.Status AS ChildStatus,
    child.FileRelativePath AS ChildPath,
    child.Sha256 AS ChildSha256,
    child.SourceImageId,
    child.ProductionStage,
    child.PromptSnapshot AS ChildPrompt,
    child.IdentityReferenceBindingsJson,
    child.AppliedReferenceBindingsJson,
    child.ModelIdentifier AS ChildModel,
    child.ProviderName AS ChildProvider,
    child.CreatedUtc AS ChildCreatedUtc,
    child.CompletedUtc AS ChildCompletedUtc,
    parent.Id AS ParentImageId,
    parent.Status AS ParentStatus,
    parent.FileRelativePath AS ParentPath,
    parent.Sha256 AS ParentSha256,
    parent.PromptSnapshot AS ParentPrompt,
    parent.ModelIdentifier AS ParentModel,
    parent.ProviderName AS ParentProvider
FROM SceneImages child
LEFT JOIN SceneImages parent ON parent.Id = child.SourceImageId
WHERE child.Id IN (
    '12c5a039-43ec-4de2-a471-e64df66ed075',
    'f8be21de-c739-4965-b3c3-2c1f34ff187b'
)
ORDER BY child.CreatedUtc;