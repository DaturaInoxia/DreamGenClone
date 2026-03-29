using DreamGenClone.Application.StoryParser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.StoryParser;

public sealed class HtmlFetchClient
{
    private readonly HttpClient _httpClient;
    private readonly StoryParserOptions _options;
    private readonly ILogger<HtmlFetchClient> _logger;

    public HtmlFetchClient(HttpClient httpClient, IOptions<StoryParserOptions> options, ILogger<HtmlFetchClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> FetchHtmlAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Request failed with status {(int)response.StatusCode}.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType) || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Non-HTML response received ({mediaType ?? "unknown"}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > _options.MaxHtmlBytes)
            {
                throw new InvalidOperationException($"Response exceeded configured maximum bytes ({_options.MaxHtmlBytes}).");
            }
        }

        var html = System.Text.Encoding.UTF8.GetString(memory.ToArray());
        _logger.LogInformation("Fetched HTML from {Url} ({Bytes} bytes)", uri, memory.Length);
        return html;
    }
}
