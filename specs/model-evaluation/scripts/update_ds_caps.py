"""Store the DeepSeek platform limits declared by the user in Model Manager."""
import sqlite3

DB = r'D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db'
con = sqlite3.connect(DB)
for model in ('deepseek-flash', 'deepseek-v4-pro'):
    cur = con.execute(
        'update RegisteredModels set MaximumContextTokens=1048576, MaximumOutputTokens=393216, '
        'SupportsThinkingControl=1 where ModelIdentifier=?', (model,))
    print(f'{model:20} rows={cur.rowcount}')

con.commit()
for row in con.execute("""select ModelIdentifier, MaximumContextTokens, MaximumOutputTokens, SupportsThinkingControl
                          from RegisteredModels where ModelIdentifier in ('deepseek-flash','deepseek-v4-pro')"""):
    print(row)
con.close()
