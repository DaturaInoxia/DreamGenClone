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
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        using var imageContent = new StreamContent(image);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", fileName);
        using var response = await client.PostAsync($"{baseUrl}/upload/image", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync(response, providerName, reasonPrefix, "upload_failed", cancellationToken);

        var upload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var name = upload?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            throw new ImageGenerationException(
                "ComfyUI returned no uploaded source image name.", providerName, reasonCode: $"{reasonPrefix}_upload_no_name");

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
                    $"ComfyUI workflow error for prompt {promptId}.", providerName, reasonCode: $"{reasonPrefix}_error");
        }

        throw new ImageGenerationException(
            $"ComfyUI timed out waiting for prompt {promptId}.", providerName, reasonCode: $"{reasonPrefix}_timeout");
    }

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
