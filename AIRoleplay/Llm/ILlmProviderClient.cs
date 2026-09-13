using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AIRoleplay;

public interface ILlmProviderClient
{
    LlmProvider Provider { get; }

    Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<LlmChatCompletionResult> CreateChatCompletionAsync(
        string modelId,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken = default,
        int maxOutputTokens = 512,
        bool requireJsonObject = false);
}
