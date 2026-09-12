namespace AIRoleplay;

public sealed record LlmResponseDiagnostics(
    LlmProvider Provider,
    int HttpStatusCode,
    long DurationMs,
    string? ResponseId,
    string? Model,
    int ChoiceCount,
    string? FinishReason,
    int ContentLength,
    int ReasoningContentLength,
    string? UsageJson,
    string RedactedRawResponsePreview)
{
    public string ToLogLine()
    {
        var status = ContentLength > 0 ? "content" : "empty";
        return "LLM_RESPONSE "
            + $"provider={Provider} "
            + $"status={status} "
            + $"http={HttpStatusCode} "
            + $"duration_ms={DurationMs} "
            + $"model={Model ?? "(unknown)"} "
            + $"choices={ChoiceCount} "
            + $"finish_reason={FinishReason ?? "(none)"} "
            + $"content_length={ContentLength} "
            + $"reasoning_length={ReasoningContentLength} "
            + $"usage={UsageJson ?? "(none)"} "
            + $"response_id={ResponseId ?? "(none)"} "
            + $"raw_preview={RedactedRawResponsePreview}";
    }
}
