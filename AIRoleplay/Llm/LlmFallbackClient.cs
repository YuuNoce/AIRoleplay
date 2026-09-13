using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace AIRoleplay;

public sealed class LlmFallbackClient : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly WindowsCredentialApiKeyReader apiKeyReader;
    private readonly Dictionary<LlmProvider, ILlmProviderClient> clients;

    public LlmFallbackClient(WindowsCredentialApiKeyReader apiKeyReader, HttpClient? httpClient = null)
    {
        this.apiKeyReader = apiKeyReader;
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        clients = new Dictionary<LlmProvider, ILlmProviderClient>
        {
            [LlmProvider.OpenAI] = new OpenAIClient(this.httpClient, apiKeyReader),
            [LlmProvider.Claude] = new ClaudeClient(this.httpClient, apiKeyReader),
            [LlmProvider.Gemini] = new GeminiClient(this.httpClient, apiKeyReader),
            [LlmProvider.DeepSeek] = new DeepSeekClient(this.httpClient, apiKeyReader),
            [LlmProvider.Zai] = new ZaiClient(this.httpClient, apiKeyReader),
        };
    }

    public bool HasAnyUsableModel(Configuration configuration) =>
        configuration.GetEnabledModelSlots()
            .Any(slot => clients.ContainsKey(slot.Provider) &&
                !string.IsNullOrWhiteSpace(slot.ModelId) &&
                apiKeyReader.HasApiKey(slot.Provider));

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(LlmProvider provider, CancellationToken cancellationToken = default)
    {
        if (!clients.TryGetValue(provider, out var client))
        {
            throw new InvalidOperationException($"Unsupported LLM provider: {provider}");
        }

        return await client.ListModelsAsync(cancellationToken);
    }

    public async Task<LlmChatCompletionResult> TestModelAsync(
        LlmProvider provider,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException("Model ID is empty.");
        }

        if (!clients.TryGetValue(provider, out var client))
        {
            throw new InvalidOperationException($"Unsupported LLM provider: {provider}");
        }

        if (!apiKeyReader.HasApiKey(provider))
        {
            throw new InvalidOperationException($"{provider} API key is not configured.");
        }

        var result = await client.CreateChatCompletionAsync(
            modelId.Trim(),
            [
                new LlmChatMessage("system", "Return only this JSON object: {\"reply\":\"OK\",\"summary\":\"\"}."),
                new LlmChatMessage("user", "Hi"),
            ],
            cancellationToken,
            requireJsonObject: true);
        if (!ReplyWithSummaryParser.TryParse(result.Content, out var parsed) ||
            !string.Equals(parsed.Reply, "OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{provider}/{modelId} returned an invalid JSON response.");
        }

        return result;
    }

    public async Task<LlmChatCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<LlmChatMessage> messages,
        Configuration configuration,
        CancellationToken cancellationToken = default,
        Action<LlmResponseDiagnostics>? onResponse = null,
        int maxOutputTokens = 512,
        Func<string, bool>? contentValidator = null)
    {
        var failures = new List<string>();
        foreach (var slot in configuration.GetEnabledModelSlots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(slot.ModelId))
            {
                continue;
            }

            if (!clients.TryGetValue(slot.Provider, out var client))
            {
                failures.Add($"{slot.Provider}/{slot.ModelId}: unsupported provider");
                continue;
            }

            if (!apiKeyReader.HasApiKey(slot.Provider))
            {
                failures.Add($"{slot.Provider}/{slot.ModelId}: API key is not configured");
                continue;
            }

            try
            {
                var result = await client.CreateChatCompletionAsync(
                    slot.ModelId, messages, cancellationToken, maxOutputTokens,
                    requireJsonObject: contentValidator != null);
                cancellationToken.ThrowIfCancellationRequested();
                onResponse?.Invoke(result.Diagnostics);
                if (!string.IsNullOrWhiteSpace(result.Content) &&
                    (contentValidator == null || contentValidator(result.Content)))
                {
                    return result;
                }

                failures.Add(string.IsNullOrWhiteSpace(result.Content)
                    ? $"{slot.Provider}/{slot.ModelId}: empty response"
                    : $"{slot.Provider}/{slot.ModelId}: response format validation failed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                failures.Add($"{slot.Provider}/{slot.ModelId}: {ex.Message}");
                Plugin.Log.Warning(ex, "LLM provider failed. Trying next configured model if available. Provider={Provider} Model={Model}", slot.Provider, slot.ModelId);
            }
        }

        throw new InvalidOperationException("All configured LLM models failed. " + string.Join(" | ", failures));
    }

    public void Dispose() => httpClient.Dispose();
}
