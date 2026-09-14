using System;
using System.Collections.Generic;
using System.Linq;

namespace AIRoleplay;

public sealed class ChatHistory
{
    private readonly object syncRoot = new();
    private readonly List<GameChatLine> recentGameChat = new();
    private readonly List<TimedLlmChatMessage> aiConversationHistory = new();
    private readonly Dictionary<string, string> conversationSummaries = new(StringComparer.Ordinal);
    private int timelineLimit = Configuration.DefaultGameChatLineLimit;
    private long nextTimelineSequence;

    public void Clear()
    {
        lock (syncRoot)
        {
            recentGameChat.Clear();
            aiConversationHistory.Clear();
            conversationSummaries.Clear();
        }
    }

    public void ClearPartyChat()
    {
        lock (syncRoot)
            recentGameChat.RemoveAll(line => ChatSafety.IsPartyChannel(line.Channel));
    }

    public GameChatLine? AddGameChatLine(
        string channel, string sender, string message, int maxLines, DateTimeOffset? timestamp = null,
        string? senderAddress = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        lock (syncRoot)
        {
            timelineLimit = Math.Max(0, maxLines);
            var line = new GameChatLine(
                channel, sender, message, timestamp ?? DateTimeOffset.UtcNow, ++nextTimelineSequence,
                senderAddress?.Trim() ?? "");
            recentGameChat.Add(line);
            TrimToLimit(recentGameChat, timelineLimit);
            TrimToLimit(aiConversationHistory, timelineLimit);
            return line;
        }
    }

    public void SetTimelineLimit(int maxEntries)
    {
        lock (syncRoot)
        {
            timelineLimit = Math.Max(0, maxEntries);
            TrimToLimit(recentGameChat, timelineLimit);
            TrimToLimit(aiConversationHistory, timelineLimit);
        }
    }

    public IReadOnlyList<GameChatLine> GetRecentGameChatSnapshot()
    {
        lock (syncRoot)
        {
            return OrderByTimeline(recentGameChat).ToList();
        }
    }

    public IReadOnlyList<string> GetFormattedRecentGameChatSnapshot()
    {
        lock (syncRoot)
        {
            return OrderByTimeline(recentGameChat)
                .Select(line => line.ToPromptLine())
                .ToList();
        }
    }

    public IReadOnlyList<string> GetRecentSendersSnapshot()
    {
        lock (syncRoot)
        {
            return OrderByTimeline(recentGameChat)
                .Select(line => line.Sender)
                .Where(sender => !string.IsNullOrWhiteSpace(sender))
                .Distinct()
                .ToList();
        }
    }

    public void AddUserMessage(string content, DateTimeOffset? timestamp = null) => AddAiMessage("user", content, timestamp);

    public void AddAssistantMessage(string content, DateTimeOffset? timestamp = null) => AddAiMessage("assistant", content, timestamp);

    public IReadOnlyList<LlmChatMessage> GetAiConversationSnapshot()
    {
        lock (syncRoot)
        {
            return OrderByTimeline(aiConversationHistory)
                .Select(message => new LlmChatMessage(message.Role, message.Content))
                .ToList();
        }
    }

    public string GetConversationSummary(string scope)
    {
        lock (syncRoot)
            return conversationSummaries.TryGetValue(scope, out var summary) ? summary : "";
    }

    public void SetConversationSummary(string scope, string summary)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;
        lock (syncRoot)
        {
            if (string.IsNullOrWhiteSpace(summary)) conversationSummaries.Remove(scope);
            else conversationSummaries[scope] = summary.Trim();
        }
    }

    public void ClearConversationSummaries(bool isPublic)
    {
        var prefix = isPublic ? "public:" : "private:";
        lock (syncRoot)
            foreach (var scope in conversationSummaries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                conversationSummaries.Remove(scope);
    }

    public IReadOnlyList<TimedLlmChatMessage> GetAiConversationTimelineSnapshot()
    {
        lock (syncRoot)
        {
            return OrderByTimeline(aiConversationHistory).ToList();
        }
    }

    private void AddAiMessage(string role, string content, DateTimeOffset? timestamp)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        lock (syncRoot)
        {
            aiConversationHistory.Add(new TimedLlmChatMessage(role, content, timestamp ?? DateTimeOffset.UtcNow, ++nextTimelineSequence));
            TrimToLimit(aiConversationHistory, timelineLimit);
        }
    }

    private static IOrderedEnumerable<T> OrderByTimeline<T>(IEnumerable<T> messages) where T : ITimelineEntry =>
        messages.OrderBy(message => message.Timestamp).ThenBy(message => message.Sequence);

    private static void TrimToLimit<T>(List<T> entries, int limit) where T : ITimelineEntry
    {
        entries.Sort((left, right) =>
            left.Timestamp != right.Timestamp
                ? left.Timestamp.CompareTo(right.Timestamp)
                : left.Sequence.CompareTo(right.Sequence));
        if (entries.Count > limit)
            entries.RemoveRange(0, entries.Count - limit);
    }
}

public interface ITimelineEntry
{
    DateTimeOffset Timestamp { get; }
    long Sequence { get; }
}

public sealed record TimedLlmChatMessage(string Role, string Content, DateTimeOffset Timestamp, long Sequence) : ITimelineEntry;

public sealed record GameChatLine(
    string Channel, string Sender, string Message, DateTimeOffset Timestamp = default, long Sequence = 0,
    string SenderAddress = "") : ITimelineEntry
{
    public string ToPromptLine()
    {
        var name = !string.IsNullOrWhiteSpace(SenderAddress)
            ? SenderAddress
            : string.IsNullOrWhiteSpace(Sender) ? Channel : Sender;
        return $"[{Channel}] {name}: {Message}";
    }
}
