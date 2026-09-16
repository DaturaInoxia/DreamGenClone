PRAGMA foreign_key_list(SceneAssetImages);
PRAGMA foreign_key_list(SceneImageEditSessions);
PRAGMA foreign_key_list(SceneImageEditCompilationAttempts);
PRAGMA foreign_key_list(SceneImageEditPromptRevisions);
SELECT name FROM sqlite_master WHERE type='table' AND sql LIKE '%SceneAssetImages%';
