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

public sealed class OpenAIClient : ILlmProviderClient
{
    private const string BaseUrl = "https://api.openai.com/v1";

    private readonly HttpClient httpClient;
    private readonly WindowsCredentialApiKeyReader apiKeyReader;

    public OpenAIClient(HttpClient httpClient, WindowsCredentialApiKeyReader apiKeyReader)
    {
        this.httpClient = httpClient;
        this.apiKeyReader = apiKeyReader;
    }

    public LlmProvider Provider => LlmProvider.OpenAI;

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
        EnsureSuccess(response, responseText, "OpenAI models");

        var models = JsonSerializer.Deserialize<OpenAIModelsResponse>(responseText, LlmJson.Options);
        var parsedModels = models?.Data?
            .Select(model => model.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => new LlmModelInfo(Provider, id, id))
            .ToList();
        return parsedModels is { Count: > 0 } ? parsedModels : throw new InvalidOperationException("No models returned; previous list was kept.");

    }

    public async Task<LlmChatCompletionResult> CreateChatCompletionAsync(
        string modelId,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken = default,
        int maxOutputTokens = 512,
        bool requireJsonObject = false)
    {
        var apiKey = ReadApiKey();
        var requestBody = new OpenAIChatRequest(
            modelId,
            messages,
            false,
            requireJsonObject ? new OpenAIResponseFormat("json_object") : null,
            maxOutputTokens);
        var json = JsonSerializer.Serialize(requestBody, LlmJson.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        EnsureSuccess(response, responseText, "OpenAI chat completion");

        var openAiResponse = JsonSerializer.Deserialize<OpenAIChatResponse>(responseText, LlmJson.Options);
        var choice = openAiResponse?.Choices is { Count: > 0 } ? openAiResponse.Choices[0] : null;
        var content = choice?.Message?.Content;
        return new LlmChatCompletionResult(
            content,
            new LlmResponseDiagnostics(
                Provider,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                openAiResponse?.Id,
                openAiResponse?.Model,
                openAiResponse?.Choices?.Count ?? 0,
                choice?.FinishReason,
                string.IsNullOrWhiteSpace(content) ? 0 : content.Length,
                0,
                openAiResponse?.Usage?.GetRawText(),
                LlmJson.BuildRedactedResponsePreview(responseText, 800)));
    }

    private string ReadApiKey()
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error ?? "OpenAI API key is not configured in Windows Credential Manager.");
        }

        return apiKey;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string responseText, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{operation} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static IReadOnlyList<LlmModelInfo> GetFallbackModels() =>
        Configuration.GetDefaultModelIds(LlmProvider.OpenAI)
            .Select(id => new LlmModelInfo(LlmProvider.OpenAI, id, id))
            .ToList();

    private sealed record OpenAIModelsResponse([property: JsonPropertyName("data")] IReadOnlyList<OpenAIModel>? Data);

    private sealed record OpenAIModel([property: JsonPropertyName("id")] string Id);

    private sealed record OpenAIChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<LlmChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [property: JsonPropertyName("response_format")] OpenAIResponseFormat? ResponseFormat,
        [property: JsonPropertyName("max_completion_tokens")] int MaxTokens);

    private sealed record OpenAIResponseFormat([property: JsonPropertyName("type")] string Type);

    private sealed record OpenAIChatResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] IReadOnlyList<OpenAIChoice>? Choices,
        [property: JsonPropertyName("usage")] JsonElement? Usage);

    private sealed record OpenAIChoice(
        [property: JsonPropertyName("finish_reason")] string? FinishReason,
        [property: JsonPropertyName("message")] OpenAIResponseMessage? Message);

    private sealed record OpenAIResponseMessage([property: JsonPropertyName("content")] string? Content);
}
