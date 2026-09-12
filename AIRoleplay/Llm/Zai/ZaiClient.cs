using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AIRoleplay;

public sealed class ZaiClient : ILlmProviderClient
{
    private const string BaseUrl = "https://api.z.ai/api/paas/v4";

    private readonly HttpClient httpClient;
    private readonly WindowsCredentialApiKeyReader apiKeyReader;

    public ZaiClient(HttpClient httpClient, WindowsCredentialApiKeyReader apiKeyReader)
    {
        this.httpClient = httpClient;
        this.apiKeyReader = apiKeyReader;
    }

    public LlmProvider Provider => LlmProvider.Zai;

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out _))
        {
            return GetFallbackModels();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.IsSuccessStatusCode)
        {
            var models = JsonSerializer.Deserialize<ZaiModelsResponse>(responseText, LlmJson.Options);
            var parsedModels = models?.Data?
                .Select(model => model.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Select(id => new LlmModelInfo(Provider, id, id))
                .ToList();
            if (parsedModels is { Count: > 0 })
            {
                return parsedModels;
            }
        }

        throw new InvalidOperationException("No models returned; previous list was kept.");
    }

    public async Task<LlmChatCompletionResult> CreateChatCompletionAsync(
        string modelId,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken = default,
        int maxOutputTokens = 512)
    {
        var apiKey = ReadApiKey();
        var requestBody = new ZaiChatRequest(
            modelId,
            messages,
            new ZaiThinking("disabled"),
            false,
            new ZaiResponseFormat("text"),
            maxOutputTokens);
        var json = JsonSerializer.Serialize(requestBody, LlmJson.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Z.ai chat completion returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var zaiResponse = JsonSerializer.Deserialize<ZaiChatResponse>(responseText, LlmJson.Options);
        var choice = zaiResponse?.Choices is { Count: > 0 } ? zaiResponse.Choices[0] : null;
        var content = choice?.Message?.Content;
        var reasoningContent = choice?.Message?.ReasoningContent;
        return new LlmChatCompletionResult(
            content,
            new LlmResponseDiagnostics(
                Provider,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                zaiResponse?.Id,
                zaiResponse?.Model,
                zaiResponse?.Choices?.Count ?? 0,
                choice?.FinishReason,
                string.IsNullOrWhiteSpace(content) ? 0 : content.Length,
                string.IsNullOrWhiteSpace(reasoningContent) ? 0 : reasoningContent.Length,
                zaiResponse?.Usage?.GetRawText(),
                LlmJson.BuildRedactedResponsePreview(responseText, 800)));
    }

    private string ReadApiKey()
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error ?? "Z.ai API key is not configured in Windows Credential Manager.");
        }

        return apiKey;
    }

    private static IReadOnlyList<LlmModelInfo> GetFallbackModels() =>
        Configuration.GetDefaultModelIds(LlmProvider.Zai)
            .Select(id => new LlmModelInfo(LlmProvider.Zai, id, id))
            .ToList();

    private sealed record ZaiModelsResponse([property: JsonPropertyName("data")] IReadOnlyList<ZaiModel>? Data);

    private sealed record ZaiModel([property: JsonPropertyName("id")] string Id);

    private sealed record ZaiChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<LlmChatMessage> Messages,
        [property: JsonPropertyName("thinking")] ZaiThinking Thinking,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("response_format")] ZaiResponseFormat ResponseFormat,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ZaiThinking([property: JsonPropertyName("type")] string Type);

    private sealed record ZaiResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record ZaiChatResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<ZaiChoice>? Choices,
        [property: JsonPropertyName("usage")] JsonElement? Usage);

    private sealed record ZaiChoice(
        [property: JsonPropertyName("finish_reason")] string? FinishReason,
        [property: JsonPropertyName("message")] ZaiResponseMessage? Message);

    private sealed record ZaiResponseMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("reasoning_content")] string? ReasoningContent);
}
