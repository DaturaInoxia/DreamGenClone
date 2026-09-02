"""Re-point Model Manager providers to the new Qwen serverless endpoints (B-101).

BaseUrl changes were already done via the guarded `provider-endpoint-update` command.
This script updates the REMAINING fields that command cannot touch:
- Qwen VL provider 2dde3563: LifecycleStrategyIdentifier -> Serverless, CredentialReference -> runpod, TimeoutSeconds -> 900
- Qwen Edit provider 0b7fb8dc: ImageProtocol -> 2 (ComfyUiServerless), CredentialReference -> runpod, TimeoutSeconds -> 900
- Qwen Edit model f264400b: DiffusionModel -> Qwen-Rapid-AIO-NSFW-v23.safetensors, Steps 8, Cfg 1,
  Sampler euler_ancestral, Scheduler beta, Denoise 1.0 (AIO merged checkpoint; TextEncoder/Vae left as-is,
  unused by the AIO workflow but resolver requires non-null).

Single SQLite connection, transactional.
"""
import sqlite3
import sys
import datetime

DB = r"D:\src\DreamGenClone\DreamGenClone.Web\data\dreamgenclone.dev.db"

VL_PROVIDER = "2dde3563-589d-436a-bc60-d646a2da3c25"
EDIT_PROVIDER = "0b7fb8dc-a07e-44ee-9418-c613a8230253"
EDIT_MODEL = "f264400b-39f1-40c9-a740-c16b44ecd343"

now = datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%S.0000000Z")

conn = sqlite3.connect(DB)
try:
    cur = conn.cursor()

    # --- Qwen VL provider ---
    cur.execute(
        "UPDATE Providers SET LifecycleStrategyIdentifier = ?, CredentialReference = ?, TimeoutSeconds = ?, UpdatedUtc = ? WHERE Id = ?",
        ("Serverless", "runpod", 900, now, VL_PROVIDER),
    )
    print(f"VL provider rows updated: {cur.rowcount}")

    # --- Qwen Edit provider ---
    cur.execute(
        "UPDATE Providers SET ImageProtocol = ?, CredentialReference = ?, TimeoutSeconds = ?, UpdatedUtc = ? WHERE Id = ?",
        (2, "runpod", 900, now, EDIT_PROVIDER),
    )
    print(f"Edit provider rows updated: {cur.rowcount}")

    # --- Qwen Edit model (AIO merged checkpoint) ---
    # RegisteredModels has no UpdatedUtc column (only CreatedUtc), so it is not touched.
    cur.execute(
        """
        UPDATE RegisteredModels
        SET ImageEditorDiffusionModel = ?,
            ImageEditorSteps = ?,
            ImageEditorCfg = ?,
            ImageEditorSampler = ?,
            ImageEditorScheduler = ?,
            ImageEditorDenoise = ?
        WHERE Id = ?
        """,
        ("Qwen-Rapid-AIO-NSFW-v23.safetensors", 8, 1.0, "euler_ancestral", "beta", 1.0, EDIT_MODEL),
    )
    print(f"Edit model rows updated: {cur.rowcount}")

    conn.commit()
    print("COMMITTED")
except Exception as e:
    conn.rollback()
    print(f"ERROR, rolled back: {e}")
    sys.exit(1)
finally:
    conn.close()
