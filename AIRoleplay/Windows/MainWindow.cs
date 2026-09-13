using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;

namespace AIRoleplay.Windows;

public class MainWindow : Window, IDisposable
{
    private const ushort AiNoticeTagColor = 43;
    private readonly object stateLock = new();
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly GameChatBridge gameChat = new();
    private readonly ConcurrentQueue<Action> completions = new();
    private CancellationTokenSource? pending;
    private volatile bool disposed;
    private long generation;
    private ulong characterId;
    private string targetIdentity = "";
    private bool wasInParty;
    private long partySessionId;
    private string inputText = "";
    private string responseText = "";
    private string replyTarget = "";
    private string replyTargetAddress = "";
    private string namedTargetInput = "";
    private string statusText = "";
    private bool draftValid;
    private bool draftPending;
    private TalkMode mode;
    private ReplyTargetMode targetMode;
    private GameChatPostChannel postChannel;
    public bool IsPublicMode => mode != TalkMode.Private;
    public bool IsPublicAssistMode => mode == TalkMode.PublicAssist;
    public bool IsPartyReply => IsPublicMode && targetMode == ReplyTargetMode.Party;
    public bool IsTellReply => IsPublicMode && postChannel == GameChatPostChannel.Tell;

    public MainWindow(Plugin plugin) : base("AIRoleplay##MainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        this.plugin = plugin;
        configuration = plugin.Configuration;
        postChannel = Enum.IsDefined(configuration.ReplyAssistPostChannel)
            ? configuration.ReplyAssistPostChannel : GameChatPostChannel.Party;
        characterId = CurrentCharacterId();
    }

    private static ulong CurrentCharacterId() =>
        Plugin.ClientState.IsLoggedIn ? Plugin.PlayerState.ContentId : 0;

    public void Dispose()
    {
        lock (stateLock)
        {
            disposed = true;
            Invalidate();
            while (completions.TryDequeue(out var complete)) complete();
        }
    }

    public void ResetToPersonalMode()
    {
        lock (stateLock)
        {
            Invalidate();
            mode = TalkMode.Private;
            targetMode = ReplyTargetMode.Conversation;
            replyTarget = "";
            replyTargetAddress = "";
            namedTargetInput = "";
            targetIdentity = "";
            wasInParty = false;
            partySessionId = 0;
            inputText = "";
            responseText = "";
            statusText = "";
            characterId = CurrentCharacterId();
        }
    }

    private void Invalidate(bool clearOwnedText = true)
    {
        generation++;
        pending?.Cancel();
        pending = null;
        gameChat.Stop(clearOwnedText);
        draftValid = false;
        draftPending = false;
    }

    private void ChangeMode(TalkMode value)
    {
        if (mode == value) return;
        Invalidate();
        mode = value;
        responseText = "";
        inputText = "";
        statusText = "";
        if (value == TalkMode.Private)
            Plugin.ChatGui.Print(T("Switched to private mode.", "プライベートモードに切り替えました。"), "AI", AiNoticeTagColor);
        else if (value == TalkMode.PublicAssist)
            Plugin.ChatGui.Print(T("Switched to public mode.", "パブリックモードに切り替えました。"), "AI", AiNoticeTagColor);
        else if (value == TalkMode.PublicDirect)
            Plugin.ChatGui.PrintError(T(
                "WARNING: Direct posting is enabled. Generated text will be sent to the game immediately without review or confirmation.",
                "警告：ダイレクト投稿が有効です。生成文は確認手順を挟まず、すぐにゲームへ送信されます。"), "AI", AiNoticeTagColor);
        plugin.ResetAutoResponseCounter();
    }

    private void ChangeTarget(ReplyTargetMode value, string target = "")
    {
        Invalidate();
        targetMode = value;
        if (value == ReplyTargetMode.Party) ChangeChannel(GameChatPostChannel.Party);
        replyTarget = "";
        replyTargetAddress = "";
        namedTargetInput = "";
        if (value == ReplyTargetMode.Named)
        {
            namedTargetInput = target;
            var trimmed = target.Trim();
            if (ChatSafety.TryNormalizeTellRecipient(trimmed, out var playerName, out var address))
            {
                replyTarget = playerName;
                replyTargetAddress = address;
            }
            else
            {
                replyTarget = trimmed;
            }
        }
        targetIdentity = "";
        RefreshTarget();
        statusText = IsTellReply && value is not (ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget)
            ? T("Tell requires an individual recipient. Choose another channel or recipient.",
                "Tellには個人の返信相手が必要です。投稿先または返信相手を変更してください。")
            : T("Recipient changed. Previous draft is inactive.", "返信相手を変更しました。以前の下書きは連動しません。");
        plugin.ResetAutoResponseCounter();
    }

    private void RefreshParty()
    {
        var isInParty = Plugin.ClientState.IsLoggedIn && Plugin.PartyList.Length > 0;
        if (isInParty == wasInParty) return;
        wasInParty = isInParty;

        if (isInParty)
        {
            partySessionId++;
            return;
        }

        plugin.ChatHistory.ClearPartyChat();
        plugin.ChatHistory.SetConversationSummary(
            $"public:{GameChatPostChannel.Party}:party:{partySessionId}", "");
        if (!IsPartyReply) return;
        Invalidate();
        plugin.ResetAutoResponseCounter();
        statusText = T("Left the party. Previous party context and draft were reset.",
            "パーティから抜けました。以前のパーティ用文脈と下書きをリセットしました。");
    }

    private void RefreshTarget()
    {
        if (!IsPublicMode || targetMode != ReplyTargetMode.CurrentTarget) return;
        var target = Plugin.TargetManager.Target as IPlayerCharacter;
        var identity = target == null ? "" : $"{target.GameObjectId}:{target.Name.TextValue}@{target.HomeWorld.RowId}";
        if (identity == targetIdentity) return;
        Invalidate();
        targetIdentity = identity;
        replyTarget = target?.Name.TextValue ?? "";
        replyTargetAddress = target != null && ChatSafety.TryNormalizeTellRecipient(
            $"{target.Name.TextValue}@{target.HomeWorld.Value.Name}", out _, out var address)
                ? address
                : "";
        statusText = T("Target changed. Previous draft is inactive.", "ターゲットが変わりました。以前の下書きは連動しません。");
    }

    public override void Update()
    {
        lock (stateLock)
        {
            if (disposed) return;
            if (characterId != CurrentCharacterId())
            {
                ResetToPersonalMode();
                plugin.ChatHistory.Clear();
                plugin.ResetAutoResponseCounter();
            }
            RefreshTarget();
            RefreshParty();
            if (gameChat.Poll())
            {
                draftPending = false;
                statusText = T("Chat input changed. Link stopped.", "ゲーム側の入力が変わったため連動を停止しました。");
            }
            while (completions.TryDequeue(out var complete)) complete();
        }
    }

    public override void Draw()
    {
        lock (stateLock)
        {
            if (disposed) return;
            ImGui.Text("AIRoleplay");
            var settingsButtonWidth = 82.0f;
            ImGui.SameLine(MathF.Max(ImGui.GetContentRegionAvail().X - settingsButtonWidth, ImGui.GetCursorPosX()));
            if (ImGui.Button(T("Settings", "設定"), new Vector2(settingsButtonWidth, 0)))
                plugin.OpenConfigUi();
            if (ImGui.RadioButton(T("Private", "プライベート"), mode == TalkMode.Private))
                ChangeMode(TalkMode.Private);
            ImGui.SameLine();
            if (ImGui.RadioButton(T("Public", "パブリック"), IsPublicMode))
                ChangeMode(TalkMode.PublicAssist);
            if (IsPublicMode)
            {
                ImGui.Indent();
                ImGui.Text(T("Public action", "パブリック動作"));
                if (ImGui.RadioButton(T("Reply assist", "返信アシスト"), mode == TalkMode.PublicAssist))
                    ChangeMode(TalkMode.PublicAssist);
                ImGui.SameLine();
                if (ImGui.RadioButton(T("Direct posting", "ダイレクト投稿"), mode == TalkMode.PublicDirect))
                    ChangeMode(TalkMode.PublicDirect);
                if (mode == TalkMode.PublicDirect)
                    ImGui.TextColored(new Vector4(1, 0.65f, 0.2f, 1),
                        T("DIRECT POSTING ENABLED", "ダイレクト投稿 有効"));
                DrawRecipientSelection();
                ImGui.BeginDisabled(targetMode == ReplyTargetMode.Party);
                ImGui.Text(T("Post channel", "投稿先"));
                ImGui.SetNextItemWidth(170);
                if (ImGui.BeginCombo("##AIRoleplayPostChannel", postChannel.ToString()))
                {
                    foreach (var channel in Enum.GetValues<GameChatPostChannel>())
                    {
                        var tellUnavailable = channel == GameChatPostChannel.Tell &&
                            targetMode is not (ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget);
                        ImGui.BeginDisabled(tellUnavailable);
                        if (ImGui.Selectable(channel.ToString(), channel == postChannel))
                            ChangeChannel(channel);
                        ImGui.EndDisabled();
                    }
                    ImGui.EndCombo();
                }
                ImGui.EndDisabled();
                if (postChannel == GameChatPostChannel.Tell)
                {
                    var address = GetReplyTargetDisplay();
                    ImGui.TextWrapped(string.IsNullOrWhiteSpace(replyTargetAddress)
                        ? T("Tell needs Firstname Lastname@World. Choose a recent speaker/current target or enter the full address.",
                            "TellにはFirstname Lastname@World形式の宛先が必要です。直近の発言者・現在のターゲットを選ぶか、完全な宛先を入力してください。")
                        : T($"Private Tell destination: {address}. Auto response only counts this player's speech.",
                            $"Tellの非公開送信先：{address}。自動応答ではこの相手本人の発言だけを数えます。"));
                }
                else if (targetMode is ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget)
                {
                    ImGui.TextDisabled(T(
                        "The recipient guides the wording; everyone in the selected channel can see the post.",
                        "返信相手は文面の対象です。投稿は選択中チャンネルの参加者に表示されます。"));
                }
                ImGui.Unindent();
            }

            ImGui.Separator();
            ImGui.Text(IsPublicMode ? T("Player's draft", "プレイヤーとしての発言") : T("AI response", "AI応答"));
            if (ImGui.InputTextMultiline("##Response", ref responseText, 16000, new Vector2(-1, 150)) &&
                mode == TalkMode.PublicAssist && draftValid && gameChat.IsLinked)
                LinkDraft();
            if (mode == TalkMode.PublicAssist)
            {
                ImGui.BeginDisabled(!draftValid || pending != null);
                if (ImGui.Button(gameChat.IsLinked ? T("Unlink", "連動停止") : T("Link to chat input", "チャット入力欄へ反映")))
                {
                    if (gameChat.IsLinked) gameChat.Stop(clearOwnedText: false);
                    else LinkDraft();
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextDisabled(gameChat.IsLinked ? T("Linked", "連動中") : T("Not linked", "連動停止中"));
            }
            if (!string.IsNullOrWhiteSpace(statusText)) ImGui.TextWrapped(statusText);
            ImGui.Separator();
            ImGui.InputTextMultiline("##Instruction", ref inputText, 4000, new Vector2(-1, 65));
            ImGui.BeginDisabled(pending != null);
            if (ImGui.Button(T("Generate", "生成")))
                StartSend(inputText, logUserInput: true);
            ImGui.EndDisabled();
            if (pending != null)
            {
                ImGui.SameLine();
                if (ImGui.Button(T("Cancel", "中止")))
                {
                    Invalidate();
                    statusText = T("Canceled.", "中止しました。");
                }
            }
            DrawContext();
        }
    }

    public void DrawRecipientSelection()
    {
        lock (stateLock)
        {
            if (!disposed) DrawRecipient();
        }
    }

    private void DrawRecipient()
    {
        ImGui.Text(T("Public reply recipient", "パブリックの返信相手"));
        if (ImGui.RadioButton(T("Whole conversation", "会話全体"), targetMode == ReplyTargetMode.Conversation))
            ChangeTarget(ReplyTargetMode.Conversation);
        if (ImGui.RadioButton(T("Party", "パーティ全体"), targetMode == ReplyTargetMode.Party))
            ChangeTarget(ReplyTargetMode.Party);
        if (targetMode == ReplyTargetMode.Party)
        {
            ImGui.TextWrapped(T(
                "Uses party chat and posts to Party. Auto response counts other members' party messages.",
                "パーティチャットを使い、Partyへ返信します。自動応答では他メンバーのパーティ発言を数えます。"));
            if (Plugin.PartyList.Length == 0)
                ImGui.TextDisabled(T("Paused: not in a party.", "一時停止中：パーティに参加していません。"));
        }
        if (ImGui.RadioButton(T("Named player", "相手を指定"), targetMode == ReplyTargetMode.Named))
            ChangeTarget(ReplyTargetMode.Named, targetMode == ReplyTargetMode.Named ? namedTargetInput : "");
        if (ImGui.RadioButton(T("Follow current target", "現在のターゲットに追従"), targetMode == ReplyTargetMode.CurrentTarget))
            ChangeTarget(ReplyTargetMode.CurrentTarget);
        if (targetMode == ReplyTargetMode.Named)
        {
            var target = namedTargetInput;
            ImGui.Text(T("Player name or Firstname Lastname@World", "プレイヤー名、またはFirstname Lastname@World"));
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##Recipient", ref target, 200)) ChangeTarget(ReplyTargetMode.Named, target);
            ImGui.SetNextItemWidth(MathF.Min(280, ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginCombo(T("Recent speakers", "直近の発言者"), GetReplyTargetDisplay()))
            {
                var senders = GetGameContext()
                    .Select(line => string.IsNullOrWhiteSpace(line.SenderAddress) ? line.Sender : line.SenderAddress)
                    .Where(sender => !string.IsNullOrWhiteSpace(sender))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (senders.Count == 0)
                    ImGui.TextDisabled(T("No captured speakers.", "取得済みの発言者がいません。"));
                foreach (var sender in senders)
                    if (ImGui.Selectable(sender, sender.Equals(GetReplyTargetDisplay(), StringComparison.OrdinalIgnoreCase)))
                        ChangeTarget(ReplyTargetMode.Named, sender);
                ImGui.EndCombo();
            }
        }
        if (targetMode == ReplyTargetMode.CurrentTarget)
        {
            var displayedTarget = IsPublicMode ? GetReplyTargetDisplay()
                : (Plugin.TargetManager.Target as IPlayerCharacter)?.Name.TextValue ?? "";
            ImGui.TextWrapped(string.IsNullOrWhiteSpace(displayedTarget)
                ? T("Paused: target a player.", "一時停止中：プレイヤーをターゲットしてください。")
                : displayedTarget);
        }
        if (targetMode != ReplyTargetMode.Conversation && !string.IsNullOrWhiteSpace(replyTarget) &&
            !GetGameContext().Any(IsLineFromReplyTarget))
            ImGui.TextWrapped(T(
                "No captured speech from this player. Manual generation is still available; their speech will not be invented.",
                "この相手の発言は取得ログにありません。手動生成はできますが、相手の発言内容は捏造させません。"));
    }

    private void ChangeChannel(GameChatPostChannel channel)
    {
        if (targetMode == ReplyTargetMode.Party && channel != GameChatPostChannel.Party)
        {
            statusText = T("Party recipient uses the Party channel.", "パーティ全体への返信先はPartyです。");
            return;
        }
        if (channel == GameChatPostChannel.Tell &&
            targetMode is not (ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget))
        {
            statusText = T("Select an individual recipient before choosing Tell.", "Tellを選ぶ前に個人の返信相手を指定してください。");
            return;
        }
        if (postChannel == channel) return;
        Invalidate();
        postChannel = channel;
        configuration.ReplyAssistPostChannel = channel;
        configuration.Save();
        statusText = T("Channel changed. Previous draft is inactive.", "投稿先を変更しました。以前の下書きは連動しません。");
    }

    public bool TryHandleModeCommand(string args)
    {
        lock (stateLock)
        {
            switch (args.ToLowerInvariant())
            {
                case "private": ChangeMode(TalkMode.Private); return true;
                case "public": case "assist": ChangeMode(TalkMode.PublicAssist); return true;
                case "direct": ChangeMode(TalkMode.PublicDirect); return true;
            }
            if (args.StartsWith("target ", StringComparison.OrdinalIgnoreCase))
            {
                var name = args[7..].Trim().Trim('"');
                if (name.Equals("current", StringComparison.OrdinalIgnoreCase)) ChangeTarget(ReplyTargetMode.CurrentTarget);
                else if (name.Equals("party", StringComparison.OrdinalIgnoreCase)) ChangeTarget(ReplyTargetMode.Party);
                else if (name.Equals("none", StringComparison.OrdinalIgnoreCase)) ChangeTarget(ReplyTargetMode.Conversation);
                else ChangeTarget(ReplyTargetMode.Named, name);
                return true;
            }
            if (args.StartsWith("channel ", StringComparison.OrdinalIgnoreCase))
            {
                var channel = args[8..].Trim().ToLowerInvariant();
                if (channel == "say") ChangeChannel(GameChatPostChannel.Say);
                else if (channel == "party") ChangeChannel(GameChatPostChannel.Party);
                else if (channel == "fc") ChangeChannel(GameChatPostChannel.FreeCompany);
                else if (channel == "tell") ChangeChannel(GameChatPostChannel.Tell);
                else statusText = "Usage: /airp channel say | party | fc | tell";
                return true;
            }
            return false;
        }
    }

    public void SubmitFromCommand(string text, bool replyAssist, string? replyTarget, bool? printToChat = null)
    {
        lock (stateLock)
        {
            if (replyAssist)
            {
                ChangeMode(TalkMode.PublicAssist);
                if (replyTarget != null)
                    ChangeTarget(replyTarget.Equals("<t>", StringComparison.OrdinalIgnoreCase)
                        ? ReplyTargetMode.CurrentTarget : ReplyTargetMode.Named, replyTarget);
            }
            StartSend(text, logUserInput: true, printToChat: printToChat);
        }
    }

    public bool SubmitFromAutoTrigger(string text, string triggerReason, bool replyAssist, string? replyTarget, bool? printToChat = null)
    {
        lock (stateLock)
        {
            if (replyAssist != IsPublicMode || (mode == TalkMode.PublicAssist && draftPending))
                return false;
            return StartSend(text, logUserInput: false, triggerReason, printToChat);
        }
    }

    public bool IsPublicAutoResponseRecipient(string sender, string senderAddress, string channel)
    {
        lock (stateLock)
        {
            if (disposed || !IsPublicMode) return false;
            return targetMode switch
            {
                ReplyTargetMode.Party => ChatSafety.IsPartyChannel(channel),
                ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget =>
                    IsReplyTarget(sender, senderAddress, requireAddress: IsTellReply),
                _ => false,
            };
        }
    }

    public MainWindowStatus GetStatus()
    {
        lock (stateLock)
        {
            Update();
            return new MainWindowStatus(mode, targetMode, GetReplyTargetDisplay(), postChannel);
        }
    }

    private bool StartSend(string rawText, bool logUserInput, string? triggerReason = null, bool? printToChat = null)
    {
        if (disposed || pending != null) return false;
        Update();
        if (!Plugin.ClientState.IsLoggedIn || CurrentCharacterId() == 0)
        {
            statusText = T("Log in first.", "ログインしてください。");
            return false;
        }
        if (IsPartyReply && Plugin.PartyList.Length == 0)
        {
            statusText = T("Join a party first.", "パーティに参加してください。");
            return false;
        }
        if (IsPublicMode && (targetMode is ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget) && string.IsNullOrWhiteSpace(replyTarget))
        {
            statusText = T("Select a reply recipient.", "返信相手を指定してください。");
            return false;
        }
        string? tellRecipient = null;
        if (IsTellReply)
        {
            if (targetMode is not (ReplyTargetMode.Named or ReplyTargetMode.CurrentTarget) ||
                !ChatSafety.TryNormalizeTellRecipient(replyTargetAddress, out _, out var address))
            {
                statusText = T(
                    "Tell requires Firstname Lastname@World. Choose a recent speaker/current target or enter the full address.",
                    "TellにはFirstname Lastname@World形式の宛先が必要です。直近の発言者・現在のターゲットを選ぶか、完全な宛先を入力してください。");
                return false;
            }
            tellRecipient = address;
        }
        var text = rawText.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            statusText = T("Input is empty.", "入力が空です。");
            return false;
        }
        string characterProfile;
        if (IsPublicMode)
        {
            if (!plugin.CharacterProfiles.TryReadCurrent(out characterProfile, out var profileError))
            {
                statusText = GetProfileFileErrorMessage(profileError);
                return false;
            }
        }
        else
        {
            characterProfile = configuration.GetLimitedCharacterPrompt();
        }
        if (string.IsNullOrWhiteSpace(characterProfile))
        {
            statusText = IsPublicMode
                ? T("Set your player character profile in /airp settings before generating.", "生成前に /airp settings でプレイヤー本人の人物像・口調を保存してください。")
                : T("Set an AI character profile in /airp settings before generating.", "生成前に /airp settings でAIキャラクター設定を保存してください。");
            return false;
        }
        if (!plugin.LlmClient.HasAnyUsableModel(configuration))
        {
            statusText = T("Configure a model and API key in /airp models.", "/airp models でモデルとAPIキーを設定してください。");
            return false;
        }
        var preserveExistingDraft = mode == TalkMode.PublicAssist && gameChat.IsLinked;
        var requestMode = mode;
        var requestTargetMode = targetMode;
        var requestPostChannel = postChannel;
        var requestRecipient = IsPartyReply
            ? T("Current party", "現在のパーティ全体")
            : targetMode == ReplyTargetMode.Conversation
                ? T("Whole conversation", "会話全体")
                : GetReplyTargetDisplay();
        var summaryScope = GetConversationSummaryScope(
            requestMode, requestTargetMode, requestPostChannel, requestRecipient);
        var useConversationSummary = requestMode == TalkMode.Private
            ? configuration.EnablePrivateConversationSummary
            : configuration.EnablePublicConversationSummary;
        var previousSummary = useConversationSummary
            ? plugin.ChatHistory.GetConversationSummary(summaryScope)
            : "";
        Invalidate(clearOwnedText: !preserveExistingDraft);
        var requestTimestamp = DateTimeOffset.UtcNow;
        var messages = BuildMessages(
            text, characterProfile, requestTimestamp, requestRecipient, requestPostChannel,
            previousSummary, useConversationSummary);
        var destination = plugin.AIRoleplayLogger.Capture(IsPublicMode);
        var requestGeneration = generation;
        var cts = new CancellationTokenSource();
        pending = cts;
        var modelConfiguration = new Configuration();
        modelConfiguration.SetModelSlots(configuration.GetEnabledModelSlots());
        var localChat = printToChat ?? configuration.ShowAiResponseInLocalChat;
        var assistantName = configuration.GetLimitedAssistantDisplayName();
        Func<string, bool> contentValidator = content => ReplyWithSummaryParser.TryParse(content, out _);
        plugin.AIRoleplayLogger.Log(destination, logUserInput ? "INPUT" : "TRIGGER", logUserInput ? text : triggerReason ?? text);
        plugin.AIRoleplayLogger.LogPrompt(destination, messages);
        if (IsPublicMode)
            plugin.AIRoleplayLogger.Log(destination, "CONTEXT",
                $"mode={requestMode} channel={requestPostChannel} recipient={requestRecipient}");
        statusText = T("Generating...", "生成中...");
        if (logUserInput) inputText = "";
        plugin.ResetAutoResponseCounter();

        _ = GenerateAsync();
        return true;

        async Task GenerateAsync()
        {
            try
            {
                var result = await plugin.LlmClient.CreateChatCompletionAsync(
                    messages,
                    modelConfiguration,
                    cts.Token,
                    diagnostics => plugin.AIRoleplayLogger.LogApiResponse(destination, diagnostics),
                    useConversationSummary ? ReplyWithSummaryParser.SummaryResponseMaxOutputTokens : 512,
                    contentValidator);
                var content = result.Content ?? "";
                if (!ReplyWithSummaryParser.TryParse(content, out var replyWithSummary))
                    throw new InvalidOperationException(T(
                        "The LLM did not return a valid reply JSON object. Nothing was displayed or sent.",
                        "LLMが有効な発言JSONを返しませんでした。表示・送信は行っていません。"));
                content = replyWithSummary.Reply;
                var updatedSummary = useConversationSummary ? replyWithSummary.Summary : "";
                completions.Enqueue(() =>
                {
                    if (disposed || requestGeneration != generation || cts.IsCancellationRequested) return;
                    RefreshTarget();
                    if (requestGeneration != generation) return;
                    pending = null;
                    if (useConversationSummary && !string.IsNullOrWhiteSpace(updatedSummary))
                        plugin.ChatHistory.SetConversationSummary(summaryScope, updatedSummary);
                    responseText = content;
                    draftValid = true;
                    draftPending = requestMode == TalkMode.PublicAssist;
                    statusText = T("Done.", "生成完了。");
                    plugin.AIRoleplayLogger.Log(destination, requestMode == TalkMode.Private
                        ? (string.IsNullOrWhiteSpace(assistantName) ? "AI" : $"AI [{assistantName}]") : "GENERATED", content);
                    if (requestMode == TalkMode.Private)
                    {
                        plugin.ChatHistory.AddUserMessage(text, requestTimestamp);
                        plugin.ChatHistory.AddAssistantMessage(content);
                    }
                    if (requestMode == TalkMode.Private && localChat)
                        Plugin.ChatGui.Print(content, string.IsNullOrWhiteSpace(assistantName) ? "AI" : $"AI/{assistantName}");
                    if (requestMode == TalkMode.PublicAssist && !preserveExistingDraft) LinkDraft();
                    if (requestMode == TalkMode.PublicDirect)
                    {
                        try
                        {
                            var command = ChatSafety.BuildCommand(content, requestPostChannel, tellRecipient);
                            gameChat.Send(command, requestPostChannel);
                            plugin.AIRoleplayLogger.Log(destination, "SEND_REQUESTED", command);
                            statusText = T("Sent to game. Delivery is not confirmed.", "ゲームへ送信操作を行いました。配信完了は未確認です。");
                        }
                        catch (Exception ex)
                        {
                            plugin.AIRoleplayLogger.Log(destination, "SEND_FAILED", ex.Message);
                            statusText = ex.Message;
                        }
                    }
                    plugin.ResetAutoResponseCounter();
                });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                plugin.AIRoleplayLogger.Log(destination, "ERROR", ex.Message);
                completions.Enqueue(() =>
                {
                    if (disposed || requestGeneration != generation) return;
                    pending = null;
                    statusText = ex.Message;
                });
            }
            finally
            {
                // Disposal is queued after result handling so token checks remain valid.
                if (disposed) cts.Dispose();
                else completions.Enqueue(cts.Dispose);
            }
        }
    }

    private void LinkDraft()
    {
        if (mode != TalkMode.PublicAssist || !draftValid) return;
        RefreshTarget();
        if (!draftValid) return;
        try
        {
            var command = ChatSafety.BuildCommand(responseText, postChannel, replyTargetAddress);
            gameChat.PutDraft(command);
            statusText = T("Ready in game chat; not sent.", "ゲームの入力欄に反映しました。まだ送信していません。");
        }
        catch (Exception ex)
        {
            gameChat.Stop(clearOwnedText: true);
            draftPending = false;
            statusText = ex.Message;
        }
    }

    private string GetReplyTargetDisplay() =>
        string.IsNullOrWhiteSpace(replyTargetAddress) ? replyTarget : replyTargetAddress;

    private bool IsLineFromReplyTarget(GameChatLine line) =>
        IsReplyTarget(line.Sender, line.SenderAddress, requireAddress: false);

    private bool IsReplyTarget(string sender, string senderAddress, bool requireAddress)
    {
        if (string.IsNullOrWhiteSpace(replyTarget)) return false;
        if (!string.IsNullOrWhiteSpace(replyTargetAddress))
        {
            if (!string.IsNullOrWhiteSpace(senderAddress))
                return string.Equals(senderAddress, replyTargetAddress, StringComparison.OrdinalIgnoreCase);
            if (requireAddress) return false;
        }
        return string.Equals(sender, replyTarget, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<GameChatLine> GetGameContext() =>
        plugin.ChatHistory.GetRecentGameChatSnapshot()
            .Where(line => IsPartyReply ? ChatSafety.IsPartyChannel(line.Channel)
                : Enum.TryParse<XivChatType>(line.Channel, out var type) && plugin.ShouldCaptureChatChannel(type))
            .ToList();

    private IReadOnlyList<LlmChatMessage> BuildMessages(
        string text, string characterProfile, DateTimeOffset requestTimestamp,
        string recipient, GameChatPostChannel channel, string conversationSummary,
        bool requestUpdatedSummary) =>
        ReplyPrompt.Build(configuration.Language, IsPublicMode,
            characterProfile,
            configuration.GetLimitedAssistantDisplayName(), plugin.PlayerContextBuilder.BuildLocalPlayerContext(configuration.Language),
            recipient, channel,
            plugin.ChatHistory.GetAiConversationTimelineSnapshot(), GetGameContext(), text, requestTimestamp,
            conversationSummary, requestUpdatedSummary);

    private string GetConversationSummaryScope(
        TalkMode requestMode, ReplyTargetMode requestTargetMode,
        GameChatPostChannel requestChannel, string requestRecipient)
    {
        if (requestMode == TalkMode.Private) return $"private:{characterId}";
        var audience = requestTargetMode switch
        {
            ReplyTargetMode.Conversation => "conversation",
            ReplyTargetMode.Party => $"party:{partySessionId}",
            _ => $"person:{requestRecipient.Trim().ToUpperInvariant()}",
        };
        return $"public:{requestChannel}:{audience}";
    }

    private string GetProfileFileErrorMessage(CharacterProfileFileError error)
    {
        return error switch
        {
            CharacterProfileFileError.NotLoggedIn => T("Log in first.", "ログインしてください。"),
            CharacterProfileFileError.NotFound => T(
                "Load or save a player character profile in /airp settings before generating.",
                "生成前に /airp settings でプレイヤー本人の人物像ファイルを読み込むか保存してください。"),
            CharacterProfileFileError.TooLong => T(
                $"The player character profile exceeds {Configuration.MaxCharacterPromptLength} characters.",
                $"プレイヤー本人の人物像ファイルが{Configuration.MaxCharacterPromptLength}文字を超えています。"),
            CharacterProfileFileError.InvalidEncoding => T(
                "The player character profile must be a valid UTF-8 text file.",
                "プレイヤー本人の人物像ファイルは正しいUTF-8のテキストにしてください。"),
            _ => T(
                "Failed to read the player character profile file.",
                "プレイヤー本人の人物像ファイルを読み込めませんでした。"),
        };
    }

    private void DrawContext()
    {
        if (!ImGui.CollapsingHeader(T("Context", "会話の文脈"))) return;
        foreach (var line in GetGameContext()) ImGui.TextWrapped(line.ToPromptLine());
        if (!IsPublicMode)
            foreach (var message in plugin.ChatHistory.GetAiConversationSnapshot())
                ImGui.TextWrapped($"{message.Role}: {message.Content}");
    }

    private string T(string english, string japanese) => UiText.T(configuration.Language, english, japanese);
}

public sealed record MainWindowStatus(
    TalkMode Mode,
    ReplyTargetMode TargetMode,
    string ReplyTarget,
    GameChatPostChannel PostChannel);
