using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Application.Abstractions;

namespace DreamGenClone.Infrastructure.Models;

/// <summary>
/// The ComfyUI HTTP conversation every workflow client shares: upload the source image, submit a prompt,
/// wait for its history entry, download the produced image. Extracted so the source-image editor and the
/// upscaler (and anything after them) speak to ComfyUI through ONE implementation instead of each carrying
/// its own copy of the polling and download rules.
///
/// <paramref name="reasonPrefix"/> keeps each caller's failure codes stable (e.g. <c>comfyui_edit_*</c> for
/// the editor, <c>comfyui_upscale_*</c> for the upscaler).
/// </summary>
internal static class ComfyUiWorkflowTransport
{
    public static async Task<string> UploadImageAsync(
        HttpClient client,
        string baseUrl,
        Stream image,
        string fileName,
        string providerName,
        string reasonPrefix,
        CancellationToken cancellationToken) =>
        await UploadInputAsync(
            client, baseUrl, image, fileName, "image/png", providerName, reasonPrefix, cancellationToken);

    /// <summary>
    /// Uploads ANY input file ComfyUI's input directory accepts. B-156 needs this because a continuation guide can
    /// carry audio as well as an image, and both go through the same <c>/upload/image</c> endpoint with
    /// <c>type=input</c> (verified against the live host: a WAV uploads exactly like a PNG and <c>LoadAudio</c> then
    /// finds it by name). One implementation, so the upload contract cannot drift between the two.
    /// </summary>
    public static async Task<string> UploadInputAsync(
        HttpClient client,
        string baseUrl,
        Stream content,
        string fileName,
        string contentType,
        string providerName,
        string reasonPrefix,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "image", fileName);
        form.Add(new StringContent("input"), "type");
        using var response = await client.PostAsync($"{baseUrl}/upload/image", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync(response, providerName, reasonPrefix, "upload_failed", cancellationToken);

        var upload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var name = upload?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            throw new ImageGenerationException(
                "ComfyUI returned no uploaded file name.", providerName, reasonCode: $"{reasonPrefix}_upload_no_name");

        var subfolder = upload?["subfolder"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(subfolder) ? name : $"{subfolder}/{name}";
    }

    public static async Task<string> SubmitPromptAsync(
        HttpClient client,
        string baseUrl,
        JsonObject workflow,
        string clientId,
        string providerName,
        string reasonPrefix,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject { ["prompt"] = workflow, ["client_id"] = clientId };
        using var submitResponse = await client.PostAsJsonAsync($"{baseUrl}/prompt", payload, cancellationToken);
        if (!submitResponse.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync(submitResponse, providerName, reasonPrefix, "submit_failed", cancellationToken);

        var submitBody = await submitResponse.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var promptId = submitBody?["prompt_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(promptId))
            throw new ImageGenerationException(
                "ComfyUI returned no prompt_id for the workflow.", providerName, reasonCode: $"{reasonPrefix}_no_prompt_id");

        return promptId;
    }

    public static async Task<JsonObject> WaitForHistoryAsync(
        HttpClient client,
        string baseUrl,
        string promptId,
        int timeoutSeconds,
        string providerName,
        string reasonPrefix,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            using var response = await client.GetAsync($"{baseUrl}/history/{promptId}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                continue;

            var history = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
            if (history?[promptId] is not JsonObject entry)
                continue;

            var status = entry["status"]?["status_str"]?.GetValue<string>();
            if (status == "success")
                return entry;
            if (status == "error")
                throw new ImageGenerationException(
                    $"ComfyUI workflow error for prompt {promptId}{DescribeExecutionError(entry)}.",
                    providerName,
                    reasonCode: $"{reasonPrefix}_error");
        }

        throw new ImageGenerationException(
            $"ComfyUI timed out waiting for prompt {promptId}.", providerName, reasonCode: $"{reasonPrefix}_timeout");
    }

    /// <summary>
    /// ComfyUI carries WHY a prompt failed in the history entry's status messages
    /// (<c>["execution_error", { node_id, node_type, exception_type, exception_message }]</c>). Returning it
    /// is the difference between an opaque "workflow error for prompt &lt;id&gt;" and the actionable cause
    /// ("ckpt_name 'x' not in [...]", "CUDA error: unknown error"), which otherwise stays buried in the
    /// host's console log. Returns <c>": node LoadImage (node 1) — torch.AcceleratorError — ..."</c>, or an
    /// empty string when ComfyUI reported no execution error at all.
    /// </summary>
    public static string DescribeExecutionError(JsonObject historyEntry)
    {
        if (historyEntry["status"]?["messages"] is not JsonArray messages)
            return string.Empty;

        foreach (var message in messages)
        {
            if (message is not JsonArray pair ||
                pair.Count < 2 ||
                ReadText(pair[0]) != "execution_error" ||
                pair[1] is not JsonObject error)
                continue;

            var nodeId = ReadText(error["node_id"]);
            var nodeType = ReadText(error["node_type"]);
            var node = string.IsNullOrWhiteSpace(nodeId) ? nodeType : $"{nodeType} (node {nodeId})";
            var detail = string.Join(
                " — ",
                new[] { node, ReadText(error["exception_type"]), CollapseWhitespace(ReadText(error["exception_message"])) }
                    .Where(part => !string.IsNullOrWhiteSpace(part)));

            if (string.IsNullOrWhiteSpace(detail))
                return string.Empty;

            return detail.Length > MaxExecutionErrorLength
                ? $" — {detail[..MaxExecutionErrorLength]}…"
                : $" — {detail}";
        }

        return string.Empty;
    }

    private const int MaxExecutionErrorLength = 600;

    private static string? ReadText(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString();

    private static string? CollapseWhitespace(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? text
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static async Task<byte[]> DownloadOutputAsync(
        HttpClient client,
        string baseUrl,
        JsonObject history,
        string promptId,
        string providerName,
        string reasonPrefix,
        CancellationToken cancellationToken)
    {
        var image = history["outputs"]?.AsObject()
            .FirstOrDefault(x => x.Value?["images"] is JsonArray).Value?["images"]?.AsArray().FirstOrDefault()?.AsObject();
        var filename = image?["filename"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(filename))
            throw new ImageGenerationException(
                $"ComfyUI produced no output image for prompt {promptId}.", providerName, reasonCode: $"{reasonPrefix}_no_output");

        var query = $"filename={Uri.EscapeDataString(filename)}";
        var subfolder = image?["subfolder"]?.GetValue<string>();
        var type = image?["type"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(subfolder)) query += $"&subfolder={Uri.EscapeDataString(subfolder)}";
        if (!string.IsNullOrWhiteSpace(type)) query += $"&type={Uri.EscapeDataString(type)}";

        using var response = await client.GetAsync($"{baseUrl}/view?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync(response, providerName, reasonPrefix, "view_failed", cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
            throw new ImageGenerationException(
                "ComfyUI returned an empty image.", providerName, reasonCode: $"{reasonPrefix}_empty_output");

        return bytes;
    }

    public static async Task<ImageGenerationException> CreateHttpExceptionAsync(
        HttpResponseMessage response,
        string providerName,
        string reasonPrefix,
        string action,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ImageGenerationException(
            $"ComfyUI request failed: {(int)response.StatusCode} {body}",
            providerName,
            (int)response.StatusCode,
            $"{reasonPrefix}_{action}");
    }
}
