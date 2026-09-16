DELETE FROM SceneAssetImageEditPromptRevisions WHERE CompilationAttemptId IN (SELECT ca.Id FROM SceneAssetImageEditCompilationAttempts ca INNER JOIN SceneAssetImageEditSessions s ON s.Id = ca.EditSessionId WHERE s.SourceImageId = '0ccfc2728bce4b7ebd32263784288308');
DELETE FROM SceneAssetImageEditCompilationAttempts WHERE EditSessionId IN (SELECT Id FROM SceneAssetImageEditSessions WHERE SourceImageId = '0ccfc2728bce4b7ebd32263784288308');
DELETE FROM SceneAssetImageEditSessions WHERE SourceImageId = '0ccfc2728bce4b7ebd32263784288308';
