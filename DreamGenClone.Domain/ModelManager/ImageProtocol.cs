namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// The wire protocol used to call an image-generation provider. Determines which client
/// implementation handles the request. Defaults to the OpenAI-compatible images endpoint.
/// </summary>
public enum ImageProtocol
{
    /// <summary>OpenAI-compatible <c>/v1/images/generations</c> endpoint (base64 response).</summary>
    OpenAiImages = 0,

    /// <summary>ComfyUI HTTP API (<c>/prompt</c> + <c>/history</c> + <c>/view</c>, workflow JSON).</summary>
    ComfyUi = 1
}
