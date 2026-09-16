SELECT name, sql FROM sqlite_master WHERE type IN ('table','trigger') AND sql LIKE '%REFERENCES SceneAssetImages%';
