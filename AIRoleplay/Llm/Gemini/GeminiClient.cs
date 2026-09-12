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

public sealed class GeminiClient : ILlmProviderClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private readonly HttpClient httpClient;
    private readonly WindowsCredentialApiKeyReader apiKeyReader;

    public GeminiClient(HttpClient httpClient, WindowsCredentialApiKeyReader apiKeyReader)
    {
        this.httpClient = httpClient;
        this.apiKeyReader = apiKeyReader;
    }

    public LlmProvider Provider => LlmProvider.Gemini;

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out _))
        {
            return GetFallbackModels();
        }

        var allModels = new List<GeminiModel>();
        var pageToken = "";
        do
        {
            var url = $"{BaseUrl}/models?pageSize=1000";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddApiKeyHeader(request, apiKey);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, responseText, "Gemini models");

            var models = JsonSerializer.Deserialize<GeminiModelsResponse>(responseText, LlmJson.Options);
            if (models?.Models is { Count: > 0 })
            {
                allModels.AddRange(models.Models);
            }

            pageToken = models?.NextPageToken ?? "";
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        var parsedModels = allModels
            .Select(model => new
            {
                Id = NormalizeModelId(model.BaseModelId, model.Name),
                model.DisplayName,
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .DistinctBy(model => model.Id)
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
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
        var requestBody = BuildRequestBody(messages, maxOutputTokens);
        var json = JsonSerializer.Serialize(requestBody, LlmJson.Options);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/models/{Uri.EscapeDataString(modelId)}:generateContent");
        AddApiKeyHeader(request, apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        EnsureSuccess(response, responseText, "Gemini generateContent");

        var geminiResponse = JsonSerializer.Deserialize<GeminiGenerateResponse>(responseText, LlmJson.Options);
        var candidate = geminiResponse?.Candidates is { Count: > 0 } ? geminiResponse.Candidates[0] : null;
        var content = string.Join("", candidate?.Content?.Parts?
            .Where(part => !string.IsNullOrWhiteSpace(part.Text))
            .Select(part => part.Text)
            ?? []);
        return new LlmChatCompletionResult(
            content,
            new LlmResponseDiagnostics(
                Provider,
                (int)response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                null,
                modelId,
                geminiResponse?.Candidates?.Count ?? 0,
                candidate?.FinishReason,
                string.IsNullOrWhiteSpace(content) ? 0 : content.Length,
                0,
                geminiResponse?.UsageMetadata?.GetRawText(),
                LlmJson.BuildRedactedResponsePreview(responseText, 800)));
    }

    private static object BuildRequestBody(IReadOnlyList<LlmChatMessage> messages, int maxOutputTokens)
    {
        var systemText = string.Join("\n\n", messages
            .Where(message => message.Role == "system")
            .Select(message => message.Content));
        var contents = messages
            .Where(message => message.Role != "system")
            .Select(message => new
            {
                role = message.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = message.Content } },
            })
            .ToList();

        if (string.IsNullOrWhiteSpace(systemText))
        {
            return new
            {
                contents,
                generationConfig = new { maxOutputTokens },
            };
        }

        return new
        {
            systemInstruction = new { parts = new[] { new { text = systemText } } },
            contents,
            generationConfig = new { maxOutputTokens },
        };
    }

    private string ReadApiKey()
    {
        if (!apiKeyReader.TryReadApiKey(Provider, out var apiKey, out var error))
        {
            throw new InvalidOperationException(error ?? "Gemini API key is not configured in Windows Credential Manager.");
        }

        return apiKey;
    }

    private static void AddApiKeyHeader(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
    }

    private static string NormalizeModelId(string? baseModelId, string? name)
    {
        if (!string.IsNullOrWhiteSpace(baseModelId))
        {
            return baseModelId;
        }

        return name?.StartsWith("models/", StringComparison.OrdinalIgnoreCase) == true
            ? name["models/".Length..]
            : name ?? "";
    }

    private static void EnsureSuccess(HttpResponseMessage response, string responseText, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{operation} returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static IReadOnlyList<LlmModelInfo> GetFallbackModels() =>
        Configuration.GetDefaultModelIds(LlmProvider.Gemini)
            .Select(id => new LlmModelInfo(LlmProvider.Gemini, id, id))
            .ToList();

    private sealed record GeminiModelsResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<GeminiModel>? Models,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);

    private sealed record GeminiModel(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("baseModelId")] string? BaseModelId,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("supportedGenerationMethods")] IReadOnlyList<string>? SupportedGenerationMethods);

    private sealed record GeminiGenerateResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("usageMetadata")] JsonElement? UsageMetadata);

    private sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiContent? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);

    private sealed record GeminiContent([property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart>? Parts);

    private sealed record GeminiPart([property: JsonPropertyName("text")] string? Text);
}
