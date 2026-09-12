using AIRoleplay;

var passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    passed++;
    Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (InvalidOperationException) { Check(true, name); return; }
    throw new Exception(name);
}

Check(ChatSafety.BuildCommand("one\r\ntwo\tthree", GameChatPostChannel.Party) == "/p one two three", "Multiline draft becomes one Party message");
Check(ChatSafety.BuildCommand(" /logout", GameChatPostChannel.Party) == "/p /logout", "Generated slash text cannot replace the channel command");
Check(ChatSafety.TryNormalizeTellRecipient(" Other Player@Example ", out var tellName, out var tellAddress) &&
    tellName == "Other Player" && tellAddress == "Other Player@Example", "Normalize a full Tell recipient");
Check(ChatSafety.BuildCommand("hello", GameChatPostChannel.Tell, tellAddress) ==
    "/tell Other Player@Example hello", "Tell command binds the recipient outside generated text");
var tellPrefixBytes = System.Text.Encoding.UTF8.GetByteCount("/tell Other Player@Example ");
Check(System.Text.Encoding.UTF8.GetByteCount(ChatSafety.BuildCommand(
    new string('a', ChatSafety.MaxChatBytes - tellPrefixBytes), GameChatPostChannel.Tell, tellAddress)) ==
    ChatSafety.MaxChatBytes, "Tell length limit includes its recipient prefix");
Reject(() => ChatSafety.BuildCommand(
    new string('a', ChatSafety.MaxChatBytes - tellPrefixBytes + 1), GameChatPostChannel.Tell, tellAddress),
    "Tell rejects text over the full command byte limit");
Reject(() => ChatSafety.BuildCommand("hello", GameChatPostChannel.Tell), "Tell rejects a missing recipient");
Reject(() => ChatSafety.BuildCommand("hello", GameChatPostChannel.Tell, "Other Player"), "Tell requires a home world");
Reject(() => ChatSafety.BuildCommand("hello", GameChatPostChannel.Tell, "Other /party@Example"), "Tell rejects command characters in recipient");
Check(ChatSafety.BuildCommand(new string('a', 497), GameChatPostChannel.Say).Length == 500, "Accept exact byte limit");
Reject(() => ChatSafety.BuildCommand(new string('a', 498), GameChatPostChannel.Say), "Reject oversized ASCII without truncation");
Reject(() => ChatSafety.BuildCommand(new string('\u3042', 166), GameChatPostChannel.Party), "Measure Japanese text in UTF-8 bytes");
Reject(() => ChatSafety.BuildCommand("a\0b", GameChatPostChannel.Party), "Reject embedded native string terminators");
Reject(() => ChatSafety.BuildCommand("<t>", GameChatPostChannel.Party), "Reject dynamic game placeholders");
Reject(() => ChatSafety.BuildCommand("text", (GameChatPostChannel)99), "Invalid channel never falls back to Say");
Reject(() => ChatSafety.BuildCommand(" \n ", GameChatPostChannel.Party), "Reject empty drafts");

var history = new ChatHistory();
history.AddUserMessage("PRIVATE_SECRET");
history.AddAssistantMessage("PRIVATE_AI_REPLY");
history.AddGameChatLine("Party", "Other Player", "hello", 20, senderAddress: "Other Player@Example");
var game = history.GetRecentGameChatSnapshot();
var promptTime = DateTimeOffset.UtcNow;
var publicPrompt = ReplyPrompt.Build(UiLanguage.English, true, "PLAYER_PROFILE", "PRIVATE_AI_NAME", "PLAYER_IDENTITY",
    "Other Player", GameChatPostChannel.Party, history.GetAiConversationTimelineSnapshot(), game, "thank them", promptTime);
var publicText = string.Join("\n", publicPrompt.Select(x => x.Content));
Check(!publicText.Contains("PRIVATE_"), "Public prompt excludes private history and AI name");
Check(publicPrompt.Count == 2 && publicText.Contains("PLAYER_PROFILE") && publicText.Contains("PLAYER_IDENTITY"), "Public prompt uses player profile and identity");
Check(publicPrompt[0].Content.Contains("[Character profile]") && !publicPrompt[0].Content.Contains("【人物設定】"), "English UI uses English LLM instructions");
Check(publicText.Contains("Other Player@Example") && publicText.Contains("Party") && publicText.Contains("hello"),
    "Public prompt retains recipient address, channel and game context");
var conversationLogPrompt = ReplyPrompt.Build(UiLanguage.Japanese, false, "PROFILE", "AI", "", "", GameChatPostChannel.Say,
    [],
    [
        new GameChatLine("NPCDialogue", "NPC A", "first", promptTime.AddSeconds(-2), 1),
        new GameChatLine("NPCDialogue", "NPC B", "second", promptTime.AddSeconds(-1), 2),
    ],
    "", promptTime);
var conversationLogText = string.Join("\n", conversationLogPrompt.Select(message => message.Content));
Check(conversationLogText.Split("【会話ログ】", StringSplitOptions.None).Length - 1 == 1 &&
    !conversationLogText.Contains("【FF14ログ】") &&
    conversationLogText.Contains("[NPCDialogue] NPC A: first") &&
    conversationLogText.Contains("[NPCDialogue] NPC B: second"),
    "Conversation log header appears once while each channel label remains");
var privatePrompt = ReplyPrompt.Build(UiLanguage.English, false, "", "PRIVATE_AI_NAME", "", "", GameChatPostChannel.Say,
    history.GetAiConversationTimelineSnapshot(), game, "hi", promptTime);
Check(privatePrompt.Any(x => x.Content.Contains("PRIVATE_SECRET")), "Private conversation retains its own history");
Check(privatePrompt[0].Content.Contains("PRIVATE_AI_NAME"), "Private mode retains AI identity");
Check(!privatePrompt[0].Content.Contains("valid JSON", StringComparison.Ordinal), "Summary disabled keeps the legacy plain-text response contract");
var summaryPrompt = ReplyPrompt.Build(UiLanguage.English, false, "PROFILE", "AI", "", "", GameChatPostChannel.Say,
    history.GetAiConversationTimelineSnapshot(), game, "hi", promptTime, "OLDER_SUMMARY", requestUpdatedSummary: true);
var summaryPromptText = string.Join("\n", summaryPrompt.Select(message => message.Content));
Check(summaryPromptText.Contains("OLDER_SUMMARY") && summaryPromptText.Contains("\"reply\"") && summaryPromptText.Contains("\"summary\""),
    "Summary enabled requests one JSON reply and carries the previous summary");
Check(ReplyWithSummaryParser.TryParse("{\"reply\":\"hello\",\"summary\":\"memory\"}", out var parsedReply) &&
    parsedReply.Reply == "hello" && parsedReply.Summary == "memory", "Parse a reply and hidden summary");
Check(ReplyWithSummaryParser.TryParse("```json\n{\"reply\":\"hello\",\"summary\":\"memory\"}\n```", out _),
    "Accept a fenced JSON response without exposing the wrapper");
Check(!ReplyWithSummaryParser.TryParse("not json", out _) &&
    !ReplyWithSummaryParser.TryParse("{\"reply\":\"hello\"}", out _), "Reject responses that could leak a missing or malformed summary");
history.SetConversationSummary("private:1", "PRIVATE_SUMMARY");
history.SetConversationSummary("public:Tell:person:OTHER PLAYER@EXAMPLE", "PUBLIC_SUMMARY");
history.ClearConversationSummaries(isPublic: false);
Check(history.GetConversationSummary("private:1") == "" &&
    history.GetConversationSummary("public:Tell:person:OTHER PLAYER@EXAMPLE") == "PUBLIC_SUMMARY",
    "Private and public summaries are isolated");
var orderedHistory = new ChatHistory();
var timelineStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
orderedHistory.AddGameChatLine("Say", "Mars", "GAME_FIRST", 20, timelineStart.AddSeconds(1));
orderedHistory.AddUserMessage("USER_FIRST", timelineStart.AddSeconds(2));
orderedHistory.AddAssistantMessage("AI_FIRST", timelineStart.AddSeconds(3));
orderedHistory.AddGameChatLine("Say", "Mars", "GAME_LATER", 20, timelineStart.AddSeconds(4));
var orderedPrompt = ReplyPrompt.Build(UiLanguage.English, false, "", "", "", "", GameChatPostChannel.Say,
    orderedHistory.GetAiConversationTimelineSnapshot(), orderedHistory.GetRecentGameChatSnapshot(), "USER_LATEST", timelineStart.AddSeconds(5));
var orderedText = string.Join("\n", orderedPrompt.Select(message => message.Content));
Check(orderedText.IndexOf("GAME_FIRST", StringComparison.Ordinal) < orderedText.IndexOf("USER_FIRST", StringComparison.Ordinal) &&
    orderedText.IndexOf("USER_FIRST", StringComparison.Ordinal) < orderedText.IndexOf("AI_FIRST", StringComparison.Ordinal) &&
    orderedText.IndexOf("AI_FIRST", StringComparison.Ordinal) < orderedText.IndexOf("GAME_LATER", StringComparison.Ordinal) &&
    orderedText.IndexOf("GAME_LATER", StringComparison.Ordinal) < orderedText.IndexOf("USER_LATEST", StringComparison.Ordinal),
    "Game logs and private conversation are merged chronologically");
history.Clear();
Check(history.GetAiConversationSnapshot().Count == 0 && history.GetRecentGameChatSnapshot().Count == 0 &&
    history.GetConversationSummary("public:Tell:person:OTHER PLAYER@EXAMPLE") == "", "Session reset clears histories and summaries");
Check(game.Count == 1, "Previously captured context is an immutable snapshot");

var defaultConfig = new Configuration();
Check(!defaultConfig.EnableFileLogging && !defaultConfig.EnablePrivateLogging && !defaultConfig.EnablePublicLogging,
    "File logging defaults to off");
var config = new Configuration { EnableFileLogging = false, CharacterPrompt = "original", PostReplyAssistToGameChat = true };
Check(config.PublicAutoResponseTrigger == PublicAutoResponseTrigger.CapturedMessageCount, "Public auto response defaults to captured message count");
Check(!config.EnablePrivateConversationSummary && !config.EnablePublicConversationSummary, "Conversation summaries default to off");
Check(ChatSafety.IsPartyChannel("Party") && ChatSafety.IsPartyChannel("CrossParty"), "Party recipient includes both party chat types");
Check(!ChatSafety.IsPartyChannel("Say") && !ChatSafety.IsPartyChannel("TellIncoming") &&
    !ChatSafety.IsPartyChannel("Alliance"), "Party recipient excludes other conversations");
var partyHistory = new ChatHistory();
partyHistory.AddGameChatLine("Party", "Old party", "old", 20);
partyHistory.AddGameChatLine("CrossParty", "Old cross party", "old", 20);
partyHistory.AddGameChatLine("Say", "Other", "retain", 20);
partyHistory.AddUserMessage("private");
partyHistory.ClearPartyChat();
Check(partyHistory.GetRecentGameChatSnapshot().Single().Channel == "Say" &&
    partyHistory.GetAiConversationSnapshot().Single().Content == "private",
    "Party changes clear only party context");
Check(config.Migrate(), "Legacy configuration migrates");
Check(!config.EnablePrivateLogging && !config.EnablePublicLogging && !config.PostReplyAssistToGameChat, "Migration retains logging preference and disarms posting");
config.EnablePublicLogging = true;
config.PublicLogDirectory = "selected-public";
Check(!config.Migrate() && config.EnablePublicLogging && config.PublicLogDirectory == "selected-public", "Migration does not overwrite separate log settings");
var fallbackConfig = new Configuration();
fallbackConfig.SetModelSlots(new[]
{
    new LlmModelSlot { Enabled = true, Provider = LlmProvider.OpenAI, ModelId = "first" },
    new LlmModelSlot { Enabled = true, Provider = LlmProvider.OpenAI, ModelId = "second" },
    new LlmModelSlot { Enabled = true, Provider = LlmProvider.OpenAI, ModelId = "third" },
});
var handler = new FakeHttpHandler("not-json", "{\"choices\":[]}", "{\"choices\":[{\"message\":{\"content\":\"reply\"}}]}");
using (var client = new LlmFallbackClient(new WindowsCredentialApiKeyReader(), new HttpClient(handler)))
{
    var diagnostics = new List<LlmResponseDiagnostics>();
    var reply = await client.CreateChatCompletionAsync(new[] { new LlmChatMessage("user", "hi") },
        fallbackConfig, onResponse: diagnostics.Add);
    Check(reply.Content == "reply" && handler.Count == 3, "Malformed JSON and empty response both fall back");
    Check(diagnostics.Count == 2 && diagnostics[0].ContentLength == 0, "Empty response diagnostics survive fallback");
}
var summaryHandler = new FakeHttpHandler(
    "{\"choices\":[{\"message\":{\"content\":\"plain\"}}]}",
    "{\"choices\":[{\"message\":{\"content\":\"{\\\"reply\\\":\\\"ok\\\",\\\"summary\\\":\\\"memory\\\"}\"}}]}");
using (var client = new LlmFallbackClient(new WindowsCredentialApiKeyReader(), new HttpClient(summaryHandler)))
{
    var reply = await client.CreateChatCompletionAsync(
        new[] { new LlmChatMessage("user", "hi") }, fallbackConfig,
        maxOutputTokens: ReplyWithSummaryParser.SummaryResponseMaxOutputTokens,
        contentValidator: content => ReplyWithSummaryParser.TryParse(content, out _));
    Check(ReplyWithSummaryParser.TryParse(reply.Content, out var parsed) && parsed.Reply == "ok" && summaryHandler.Count == 2,
        "Invalid summary JSON falls back to the next configured model");
    Check(summaryHandler.RequestBodies.All(body => body.Contains("\"max_completion_tokens\":4096", StringComparison.Ordinal)),
        "Summary generation receives a larger output-token limit");
}
var canceledHandler = new FakeHttpHandler("{}");
using (var client = new LlmFallbackClient(new WindowsCredentialApiKeyReader(), new HttpClient(canceledHandler)))
{
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try
    {
        await client.CreateChatCompletionAsync(Array.Empty<LlmChatMessage>(), fallbackConfig, canceled.Token);
        throw new Exception("Cancellation was swallowed");
    }
    catch (OperationCanceledException) { Check(canceledHandler.Count == 0, "Canceled request never tries another provider"); }
}
var errorHandler = new FakeHttpHandler("{}") { Status = System.Net.HttpStatusCode.Unauthorized };
var providerClient = new OpenAIClient(new HttpClient(errorHandler), new WindowsCredentialApiKeyReader());
try
{
    await providerClient.ListModelsAsync();
    throw new Exception("Failed refresh was reported as success");
}
catch (HttpRequestException) { Check(true, "Failed model refresh is reported to the UI"); }
Console.WriteLine($"Passed {passed} checks.");

sealed class FakeHttpHandler(params string[] responses) : HttpMessageHandler
{
    public int Count { get; private set; }
    public List<string> RequestBodies { get; } = new();
    public System.Net.HttpStatusCode Status { get; init; } = System.Net.HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Content != null) RequestBodies.Add(await request.Content.ReadAsStringAsync(token));
        return new HttpResponseMessage(Status) { Content = new StringContent(responses[Count++]) };
    }
}
