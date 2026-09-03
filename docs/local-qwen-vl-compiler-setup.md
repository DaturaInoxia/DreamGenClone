# Move the Qwen VL compiler to local LM Studio (jer host)

> **Goal:** stop paying ~$15/day for the RunPod `compiler-qwen-vl-serverless` endpoint by serving the
> same model locally on the **jer** machine (RTX 5080 16 GB) via **LM Studio**, and re-pointing the
> app at it over the LAN.
>
> **Host note:** the host that currently runs LM Studio is **WOOD-GAME-MAIN** (RTX 5080 16 GB,
> driver 591.86, `http://192.168.0.16:1234`). This doc is the agent notes used to re-point the Model
> Manager provider/model in the target environment's database (the live `dreamgenclone.dev.db` on the
> machine that runs the webapp).
>
> **What the model does (why this matters):** the vision compiler is the *front half* of every image
> edit — it reads the source image + your edit intent and produces the structured JSON that drives
> Qwen-Image-Edit. It is consumed by **two** app functions (both resolve to the same model):
> - `RolePlaySceneImageEditPromptCompiler` — edit-intent compilation (required for edits)
> - `RolePlaySceneImageValidator` — edit-result validation
>
> One provider re-point covers both. The "analyze on open" description also uses this model but is
> optional/informational.
>
> **Why local works when serverless "needed" an A100:** the serverless endpoint serves the model at
> **BF16 (~16.6 GB)** through vLLM (pinned to CUDA 13.0), which OOM'd 24 GB GPUs and forced an
> A100 80 GB. Locally we load a **Q4_K_M GGUF (~6 GB)** through llama.cpp — same model, ~9 GB total
> footprint, fits a 16 GB RTX 5080 easily. See `docs/` spend analysis history for the billing detail.

---

## Verified status (2026-08-31)

Part A is DONE and verified on the host:

- Model downloaded: `Qwen2.5-VL-7B-Instruct-abliterated.Q4_K_M.gguf` (4,466 MB) +
  `...mmproj-f16.gguf` (1,291 MB) → `D:\LMStudio\Models\mradermacher\Qwen2.5-VL-7B-Instruct-abliterated-GGUF\`
- Loaded: `lms load qwen2.5-vl-7b-instruct-abliterated --gpu max -c 8192` → 6.04 GB, ctx **8192**, Local/GPU
- Served on LAN: bound `0.0.0.0:1234`; LAN IP **`192.168.0.16`**
- Served model id (API): **`qwen2.5-vl-7b-instruct-abliterated`**
- Pre-flight (1×1 PNG) and a **realistic full compile test** both pass transport, `json_schema`,
  image input, and model echo. Realistic test: `helpers/qwen-vl-local-compiler-test.ps1`.

**✅ Integration gap FIXED (2026-08-31):** the model returns target `region` as **pixel
coordinates**, not normalized 0–1. The app now normalizes pixel regions using the known source image
dimensions (see "Verified findings" below and debug record
`specs/001-rp-prompt-redesign/debug/038-scene-image-edit-compiler-pixel-region-normalization.md`).
Verified end-to-end against this endpoint: pixel regions are accepted and compile to `Ready`.

---

## Part A — On the jer host (LM Studio + model)

### A1. Verify the host

```powershell
# RTX 5080 = 16 GB VRAM expected. Confirm:
nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader
# LM Studio server is reachable locally:
Invoke-RestMethod http://localhost:1234/v1/models
```

> **2026-08-31 verified:** `NVIDIA GeForce RTX 5080, 16303 MiB, 591.86`; LM Studio responds on
> `localhost:1234`.

### A2. Download the model (GGUF Q4_K_M + mmproj)

In LM Studio's model search (it searches HuggingFace), find one of these GGUF quants of the
**abliterated** model (do **not** use the stock aligned model — it refuses adult edits):

- **Recommended:** `mradermacher/Qwen2.5-VL-7B-Instruct-abliterated-GGUF` → download the **Q4_K_M**
  file (~4.4 GB). This repo also contains the vision projector (`mmproj-*.gguf`) — LM Studio downloads
  it automatically for Qwen2.5-VL.
- **Alternative (higher fidelity, larger):** `dthryjdrk/Qwen2.5-VL-7B-Instruct-abliterated-Q8_0-GGUF`
  (~8.5 GB, Q8_0).

Qwen2.5-VL in LM Studio needs **both** the LLM GGUF and the mmproj. If the download only fetched the
LLM file, also grab the `mmproj` file from the same repo and select it as the model's projector.

> **2026-08-31 (scripted path used, no GUI needed):** LM Studio's `downloadsFolder` is
> `D:\LMStudio\Models` (see `%USERPROFILE%\.lmstudio\settings.json`). Files were placed directly at
> `D:\LMStudio\Models\mradermacher\Qwen2.5-VL-7B-Instruct-abliterated-GGUF\`:
> ```powershell
> $dir = "D:\LMStudio\Models\mradermacher\Qwen2.5-VL-7B-Instruct-abliterated-GGUF"
> $base = "https://huggingface.co/mradermacher/Qwen2.5-VL-7B-Instruct-abliterated-GGUF/resolve/main"
> curl.exe -L --fail -o "$dir\Qwen2.5-VL-7B-Instruct-abliterated.Q4_K_M.gguf" "$base/Qwen2.5-VL-7B-Instruct-abliterated.Q4_K_M.gguf"
> curl.exe -L --fail -o "$dir\Qwen2.5-VL-7B-Instruct-abliterated.mmproj-f16.gguf" "$base/Qwen2.5-VL-7B-Instruct-abliterated.mmproj-f16.gguf"
> ```

### A3. Load it

- Context length: **8192** (matches the app's `ContextWindowSize`).
- GPU offload: **max** (full VRAM). On 16 GB this leaves plenty of headroom.

> **2026-08-31 (CLI path):**
> ```powershell
> & "$env:USERPROFILE\.lmstudio\bin\lms.exe" load qwen2.5-vl-7b-instruct-abliterated --gpu max -c 8192 -y
> & "$env:USERPROFILE\.lmstudio\bin\lms.exe" ps   # confirm IDLE, 6.04 GB, 8192, Local
> ```

### A4. Serve on the LAN

- **Settings → Server (or Developer tab) → "Serve on Local Network"** → enable; port **1234**.
- Find the machine's LAN IP for Part B:
  ```powershell
  ipconfig | findstr /i "IPv4"
  ```

> **2026-08-31 verified:** port `1234` bound on `0.0.0.0` (LAN-reachable); LAN IP **`192.168.0.16`**
> (Wi-Fi).

### A5. Capture the exact served model id

```powershell
Invoke-RestMethod http://localhost:1234/v1/models | ConvertTo-Json -Depth 5
```

The `data[].id` value is the **exact string the app must register** as `ModelIdentifier` (and use in
the readiness contract). It must match what the endpoint echoes in the response `model` field.

> **2026-08-31 verified:** `data[].id` = **`qwen2.5-vl-7b-instruct-abliterated`**.

### A6. Pre-flight test (proves json_schema + image + model echo before touching the app)

Fire the app's exact request shape at LM Studio. Replace `<MODEL_ID>` with the id from A5 and
`<BASE64>` with any small image (a 1x1 PNG is fine — this test only proves the transport/contract):

```powershell
$body = @{
  model      = "<MODEL_ID>"
  messages   = @(
    @{ role = "system"; content = "You are a vision compiler. Return only valid JSON." },
    @{ role = "user"; content = @(
        @{ type = "text"; text = "Describe what changed." },
        @{ type = "image_url"; image_url = @{ url = "data:image/png;base64,<BASE64>" } }
    ) }
  )
  temperature = 0.2
  top_p       = 0.8
  max_tokens  = 256
  response_format = @{
    type        = "json_schema"
    json_schema = @{
      name   = "scene_image_edit_compilation"
      strict = $true
      schema = @{
        type = "object"
        additionalProperties = $false
        required = @("summary")
        properties = @{ summary = @{ type = "string" } }
      }
    }
  }
} | ConvertTo-Json -Depth 12

# NOTE: send the body as UTF-8 bytes — PS 5.1 can corrupt multibyte JSON from a plain string body.
$resp = Invoke-RestMethod -Uri "http://localhost:1234/v1/chat/completions" `
    -Method Post -ContentType "application/json" -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
```

**Pass criteria:**
1. HTTP 200 with `choices[0].message.content` containing JSON matching the schema.
2. `resp.model` equals `<MODEL_ID>` exactly.

> **2026-08-31 verified: PASSED.** HTTP 200; echoed model `qwen2.5-vl-7b-instruct-abliterated`;
> valid JSON output. (Note: send the body as UTF-8 bytes — PS 5.1 `Invoke-RestMethod` can corrupt
> multibyte JSON when passed a plain string body.)
>
> **Realistic end-to-end test (what the app actually does):** `helpers/qwen-vl-local-compiler-test.ps1`
> replicates `QwenSceneImageEditPromptCompiler` (verbatim system prompt, user format, full schema,
> strict `Parse` rules) with a real scene image. It PASSES transport/json_schema/echo but **FAILS the
> app's region-bounds check** — see "Verified findings" below.

---

## Part B — On the dev machine (WOODGAME): re-point the app

Do this in the **Model Manager UI** (`/model-manager`) — it encrypts the API key for you and is the
UI-backed config path. (A scripted SQL alternative is in the appendix.)

### B1. Edit the provider

Find provider **"RunPod Qwen VL image compiler"** and set:

| Field | New value |
|---|---|
| BaseUrl | **`http://192.168.0.16:1234`** (bare host — NO `/v1` suffix; see warning below) |
| ChatCompletionsPath | `/v1/chat/completions` (unchanged) |
| LifecycleStrategy | **AlwaysOnSeparateProvider** (NOT Serverless — Serverless skips the readiness probe) |
| ReadinessPath | `/v1/models` |
| ReadinessSuccessContractJson | `{"object":"list","data":[{"id":"qwen2.5-vl-7b-instruct-abliterated"}]}` |
| CredentialReference | `lmstudio-local` |
| API key | any non-empty value, e.g. `lmstudio-local` (LM Studio ignores auth) |
| TimeoutSeconds | `300` |

Save.

> **⚠️ BaseUrl must NOT end in `/v1` (verified against `OpenAiMultimodalCompletionClient`).**
> The client builds the request URL as `BaseUrl/` + `ChatCompletionsPath` (leading `/` stripped) and
> `BaseUrl/` + `ReadinessPath` for health. With `BaseUrl = http://192.168.0.16:1234/v1` the final URLs
> become `http://192.168.0.16:1234/v1/v1/chat/completions` and `.../v1/v1/models` — **doubled `/v1`**,
> which 404s. Use the bare host `http://192.168.0.16:1234`; the `/v1` comes from the path fields.

### B2. Edit the model

Find model **"Qwen2.5-VL 7B abliterated image compiler"** and set:

- **ModelIdentifier** → `qwen2.5-vl-7b-instruct-abliterated` (must equal the provider contract and the
  response `model` echo). Leave `SupportsImageInput`, image limits, etc. unchanged.

Save.

### B3. Function defaults — one config change: MaxTokens ≥ 512

`RolePlaySceneImageEditPromptCompiler` and `RolePlaySceneImageValidator` already point at this model;
both now resolve to the local endpoint automatically.

> **Set `RolePlaySceneImageEditPromptCompiler` MaxTokens to ≥ 512** (Model Manager → Function
> Defaults). The local model can be verbose; at 256 output tokens the response can be truncated
> mid-JSON, which fails the compiler parse. This is a UI/config-only change (no code).
>
> `RolePlaySceneImageValidator` can keep its current MaxTokens unless edits fail to validate.

### B4. No restart needed

Model resolution reads the DB per request; the next edit/validate uses the local endpoint. (The
running webapp process can stay up.)

---

## Part C — Verify end-to-end

1. Open the image editor on an image and **Prepare an edit**. It should complete in roughly
   **8–20 s** locally (no cold-start).
2. Confirm the "What the model sees" description and the compiled result appear as before.
3. Next day, re-run the spend query and confirm the compiler endpoint (`n09tv90559x2cu`) is ~$0.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File helpers/runpod/runpod-billing-query.ps1 -StartTime "<yesterday>T00:00:00Z" -EndTime "<today>T00:00:00Z" -BucketSize day
```

---

## Verified findings (2026-08-31)

1. **Transport/contract fully works.** json_schema response format, image input, model echo, and LAN
   reachability all pass against `http://192.168.0.16:1234`. Latency ~3.5 s for a 1.35 MB image.

2. **Target `region` comes back as PIXEL coordinates, not normalized 0–1 — FIXED.**
   `QwenSceneImageEditPromptCompiler.ParseRegion` requires `x/y/width/height` normalized and contained
   in `[0,1]` (`x+width ≤ 1`, etc.). The local model emits values like `{"x":690,"y":435,"width":720,
   "height":180}` (pixels; `x+width = 1410 > 1`). On RunPod/vLLM the structured-output layer enforced
   `maximum: 1`; LM Studio's llama.cpp **grammar does not enforce numeric min/max**, so the model emits
   pixels.
   **Fix (implemented 2026-08-31):** `Parse` now receives the source image dimensions
   (`SceneImageEditCompilationJobHandler` passes `input.Width`/`input.Height`); `ParseRegion` detects
   pixel-scale regions (any value > 1), divides by the matching dimension, and clamps into `[0,1]`.
   Already-normalized and `null` regions are unchanged; a pixel region with no dimensions fails fast.
   Verified: full test suite green (1418), live retest against this endpoint returns `Ready` with
   normalized regions. Regression tests in `QwenSceneImageEditPromptCompilerTests`.

3. **BaseUrl must be the bare host** (`http://192.168.0.16:1234`), not `.../v1` — see B1 warning.

4. **Useful commands:** `lms ls` / `lms load <id> --gpu max -c 8192 -y` / `lms ps` (CLI at
   `%USERPROFILE%\.lmstudio\bin\lms.exe`). Model folder: `D:\LMStudio\Models\mradermacher\...`.

5. **The region never reaches the Qwen Edit model — only the compiled prompt does.**
   `SceneImageEditingJobHandler` calls `EditAsync(sourceImage, compiledPrompt)`; the compiler's
   `targets[].region` is app-internal metadata (stored in `ParsedResultJson`), not an input to the
   edit model. So the region's format is irrelevant to Qwen Edit — what matters is that the app's
   `ParseRegion` accepts it (now fixed by normalization) and that the `compiledPrompt` is clean
   text (verified live: e.g. `"Change the woman's red tank top to black."` — no coordinates).

---

## Rollback (restore RunPod serverless)

In Model Manager, revert the same fields:

| Field | RunPod value |
|---|---|
| BaseUrl | `https://api.runpod.ai/v2/n09tv90559x2cu/openai` |
| LifecycleStrategy | `Serverless` |
| ReadinessSuccessContractJson | `{"object":"list","data":[{"id":"huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated"}]}` |
| CredentialReference | `runpod` |
| ModelIdentifier | `huihui-ai/Qwen2.5-VL-7B-Instruct-abliterated` |
| API key | the RunPod API key (re-enter it — saving a new key overwrites the encrypted value) |

> To revert the local re-point, first restore BaseUrl to `http://192.168.0.16:1234` and
> `ReadinessSuccessContractJson` to the local contract above, then apply the RunPod row.

---

## Appendix — scripted SQL (for an agent; UI is preferred)

The built-in DB query tool's `sql` command now opens the dev DB **ReadWrite**, so an agent can apply
the re-point with these UPDATEs (run each statement as its own `.sql` file via `dbq sql`, or the
equivalent). The API key must be **DPAPI-encrypted on WOODGAME** (same Windows user that runs the
webapp), which only PowerShell does easily — so step 1 stays PowerShell, then paste the base64 into
the UPDATE.

1. Encrypt a dummy key (run on WOODGAME, as the webapp's user):

   ```powershell
   Add-Type -AssemblyName System.Security
   $b = [System.Text.Encoding]::UTF8.GetBytes('lmstudio-local')
   $enc = [System.Security.Cryptography.ProtectedData]::Protect($b, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
   [Convert]::ToBase64String($enc)   # paste the output into the UPDATE below
   ```

2. Apply (against `DreamGenClone.Web/data/dreamgenclone.dev.db`, ReadWrite):

   ```sql
   UPDATE Providers
   SET BaseUrl = 'http://192.168.0.16:1234',
       LifecycleStrategyIdentifier = 'AlwaysOnSeparateProvider',
       ReadinessPath = '/v1/models',
       ReadinessSuccessContractJson = '{"object":"list","data":[{"id":"qwen2.5-vl-7b-instruct-abliterated"}]}',
       CredentialReference = 'lmstudio-local',
       ApiKeyEncrypted = '<DPAPI base64 from step 1>',
       TimeoutSeconds = 300,
       UpdatedUtc = '<utc now ISO8601>'
   WHERE Id = '2dde3563-589d-436a-bc60-d646a2da3c25';

   UPDATE RegisteredModels
   SET ModelIdentifier = 'qwen2.5-vl-7b-instruct-abliterated'
   WHERE Id = 'db602892-d604-40b1-8f7d-7d6073f7fe1d';
   ```

   > If the live DB's provider row has a different `ChatCompletionsPath` than `/v1/chat/completions`,
   > keep it unchanged — the BaseUrl above is the bare host specifically so the `/v1` path suffix is
   > NOT doubled by the client.
