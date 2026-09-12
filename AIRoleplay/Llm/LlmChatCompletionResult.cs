namespace AIRoleplay;

public sealed record LlmChatCompletionResult(string? Content, LlmResponseDiagnostics Diagnostics);
