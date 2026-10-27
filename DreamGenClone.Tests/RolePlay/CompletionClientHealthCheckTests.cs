using System.Net;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Infrastructure.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Health checks must never bill the account: no completion and no image may be requested. These tests pin
/// that contract to the provider's model list.
/// </summary>
public sealed class CompletionClientHealthCheckTests
{
    private static CompletionClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> responder, out List<HttpRequestMessage> requests)
    {
        var captured = new List<HttpRequestMessage>();
        requests = captured;
        var handler = new StubHttpMessageHandler(req =>
        {
            captured.Add(req);
            return responder(req);
        });
        return new CompletionClient(new FakeHttpClientFactory(handler), new FakeEncryption(), NullLogger<CompletionClient>.Instance);
    }

    private static HttpResponseMessage ModelList(params string[] ids)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data = ids.Select(id => new { id }) }))
        };

    [Fact]
    public async Task CheckModelHealthAsync_ReadsModelList_AndNeverRequestsACompletion()
    {
        var client = BuildClient(_ => ModelList("deepseek-test"), out var requests);

        var (success, message) = await client.CheckModelHealthAsync(
            "https://api.together.ai", "/v1/chat/completions", 30, "sekret", "deepseek-test", CancellationToken.None);

        Assert.True(success);
        Assert.Contains("No completion was requested", message);
        Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Single(requests);
        Assert.Equal("https://api.together.ai/v1/models", requests[0].RequestUri!.ToString());
        Assert.Equal("sekret", requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CheckModelHealthAsync_KeyRejected_ReturnsFailure()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized), out var requests);

        var (success, message) = await client.CheckModelHealthAsync(
            "https://api.together.ai", "/v1/chat/completions", 30, "sekret", "deepseek-test", CancellationToken.None);

        Assert.False(success);
        Assert.Contains("rejected", message, StringComparison.OrdinalIgnoreCase);
        Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task CheckModelHealthAsync_ModelMissingFromList_ReturnsFailure()
    {
        var client = BuildClient(_ => ModelList("some-other-model"), out _);

        var (success, message) = await client.CheckModelHealthAsync(
            "https://api.together.ai", "/v1/chat/completions", 30, "sekret", "deepseek-test", CancellationToken.None);

        Assert.False(success);
        Assert.Contains("not in the provider's model list", message);
    }

    [Fact]
    public async Task CheckModelHealthAsync_NoListingEndpoint_FallsBackToReachabilityPing()
    {
        var client = BuildClient(
            req => req.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK),
            out var requests);

        var (success, message) = await client.CheckModelHealthAsync(
            "https://api.together.ai", "/v1/chat/completions", 30, "sekret", "deepseek-test", CancellationToken.None);

        Assert.True(success);
        Assert.Contains("No completion was requested", message);
        Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task CheckHealthAsync_RejectedKey_ReturnsFalseWithoutRequestingACompletion()
    {
        var client = BuildClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), out var requests);

        var healthy = await client.CheckHealthAsync("https://api.together.ai", 30, "sekret", CancellationToken.None);

        Assert.False(healthy);
        Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task CheckHealthAsync_ModelListPasses_ReturnsTrueWithoutACompletion()
    {
        var client = BuildClient(_ => ModelList("deepseek-test"), out var requests);

        var healthy = await client.CheckHealthAsync("https://api.together.ai", 30, "sekret", CancellationToken.None);

        Assert.True(healthy);
        Assert.Single(requests);
        Assert.All(requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    private sealed class FakeEncryption : IApiKeyEncryptionService
    {
        public string Encrypt(string plainTextApiKey) => plainTextApiKey;
        public string Decrypt(string encryptedApiKey) => encryptedApiKey;
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
