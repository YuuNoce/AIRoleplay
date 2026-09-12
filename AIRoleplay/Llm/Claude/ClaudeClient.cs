using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AIRoleplay;

public sealed class ClaudeClient : ILlmProviderClient
{
    private const string BaseUrl = "https://api.anthropic.com/v1";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient httpClient;
    private readonly WindowsCredentialApiKeyReader apiKeyReader;

    public ClaudeClient(HttpClient httpClient, WindowsCredentialApiKeyReader apiKeyReader)
    {
        this.httpClient = httpClient;
        this.apiKeyReader = apiKeyReader;
    }

    public LlmProvider Provider => LlmProvider.Claude;

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out _))
        {
            return GetFallbackModels();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models?limit=1000");
        AddHeaders(request, apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseText, "Claude models");

        var models = JsonSerializer.Deserialize<ClaudeModelsResponse>(responseText, LlmJson.Options);
        var parsedModels = models?.Data?
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new LlmModelInfo(Provider, model.Id, string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName))
            .ToList();
        return parsedModels is { Count: > 0 } ? parsedModels : throw new InvalidOperationException("No models returned; previous list was kept.");

    }

    public async Task<LlmChatCompletionResult> CreateChatCompletionAsync(
        string modelId,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken = default,
        int maxOutputTokens = 512)
    {
        var apiKey = ReadApiKey();
        var system = string.Join("\n\n", messages.Where(message => message.Role == "system").Select(message => message.Content));
        var claudeMessages = messages
            .Where(message => message.Role != "system")
            .Select(message => new ClaudeRequestMessage(
                message.Role == "assistant" ? "assistant" : "user",
                message.Content))
            .ToList();
        var requestBody = new ClaudeMessageRequest(
            modelId,
            string.IsNullOrWhiteSpace(system) ? null : system,
            claudeMessages,
            maxOutputTokens,
            false);
        var json = JsonSerializer.Serialize(requestBody, LlmJson.Options);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/messages");
        AddHeaders(request, apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        EnsureSuccess(response, responseText, "Claude message");

        var claudeResponse = JsonSerializer.Deserialize<ClaudeMessageResponse>(responseText, LlmJson.Options);
        var content = string.Join("", claudeResponse?.Content?
            .Where(block => block.Type == "text" && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => block.Text)
            ?? []);
        return new LlmChatCompletionResult(
            content,
            new LlmResponseDiagnostics(
                Provider,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                claudeResponse?.Id,
                claudeResponse?.Model,
                claudeResponse?.Content?.Count ?? 0,
                claudeResponse?.StopReason,
                string.IsNullOrWhiteSpace(content) ? 0 : content.Length,
                0,
                claudeResponse?.Usage?.GetRawText(),
                LlmJson.BuildRedactedResponsePreview(responseText, 800)));
    }

    private string ReadApiKey()
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error ?? "Claude API key is not configured in Windows Credential Manager.");
        }

        return apiKey;
    }

    private static void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string responseText, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{operation} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static IReadOnlyList<LlmModelInfo> GetFallbackModels() =>
        Configuration.GetDefaultModelIds(LlmProvider.Claude)
            .Select(id => new LlmModelInfo(LlmProvider.Claude, id, id))
            .ToList();

    private sealed record ClaudeModelsResponse([property: JsonPropertyName("data")] IReadOnlyList<ClaudeModel>? Data);

    private sealed record ClaudeModel(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record ClaudeMessageRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [property: JsonPropertyName("system")] string? System,
        [property: JsonPropertyName("messages")] IReadOnlyList<ClaudeRequestMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record ClaudeRequestMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ClaudeMessageResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock>? Content,
        [property: JsonPropertyName("stop_reason")] string? StopReason,
        [property: JsonPropertyName("usage")] JsonElement? Usage);

    private sealed record ClaudeContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
