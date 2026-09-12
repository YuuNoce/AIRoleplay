using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using AIRoleplay.Windows;
using System.Linq;
using Dalamud.Game.Text.SeStringHandling.Payloads;

namespace AIRoleplay;

public sealed class Plugin : IDalamudPlugin
{
    private const ushort AiNoticeTagColor = 43;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;

    private const string CommandName = "/airp";
    public Configuration Configuration { get; init; }
    public ChatHistory ChatHistory { get; } = new();
    public WindowsCredentialApiKeyReader ApiKeyReader { get; } = new();
    public LlmFallbackClient LlmClient { get; }
    public AIRoleplayLogger AIRoleplayLogger { get; }
    public CharacterProfileStore CharacterProfiles { get; } = new();
    public PlayerContextBuilder PlayerContextBuilder { get; } = new();
    public LogSettingsPanel LogSettings { get; }

    public readonly WindowSystem WindowSystem = new("AIRoleplay");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private ModelSettingsWindow ModelSettingsWindow { get; init; }
    private int personalAutoResponseCapturedMessageCount;
    private int publicAutoResponseCapturedMessageCount;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        var shouldSaveConfiguration = Configuration.Migrate();
        if (Configuration.PostReplyAssistToGameChat)
        {
            Configuration.PostReplyAssistToGameChat = false;
            shouldSaveConfiguration = true;
        }

        if (shouldSaveConfiguration)
        {
            Configuration.Save();
        }

        LlmClient = new LlmFallbackClient(ApiKeyReader);
        AIRoleplayLogger = new AIRoleplayLogger(Configuration);
        LogSettings = new LogSettingsPanel(this);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        ModelSettingsWindow = new ModelSettingsWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ModelSettingsWindow);
        ResetSessionSafetyState(saveConfiguration: false);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = T("Open AIRoleplay or send text. Use /airp help for commands.",
                "AIRoleplayを開く、またはテキストを送信します。コマンド一覧は /airp help。")
        });

        ChatGui.ChatMessage += OnChatMessage;
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += LogSettings.DrawDialogs;
        PluginInterface.UiBuilder.Draw += ConfigWindow.DrawDialogs;

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information($"{PluginInterface.Manifest.Name} loaded.");
        if (Configuration.EnablePersonalAutoResponseByChatLines)
        {
            ChatGui.Print(
                T(
                    "Personal auto response is enabled. LLM requests may be sent automatically while you play.",
                    "パーソナル自動応答が有効です。プレイ中にLLMリクエストが自動送信される可能性があります。"),
                "AI",
                AiNoticeTagColor);
        }

    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        ClientState.Login -= OnLogin;
        ClientState.Logout -= OnLogout;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= LogSettings.DrawDialogs;
        PluginInterface.UiBuilder.Draw -= ConfigWindow.DrawDialogs;
        ChatGui.ChatMessage -= OnChatMessage;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        ModelSettingsWindow.Dispose();
        LlmClient.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim();
        if (string.IsNullOrWhiteSpace(args))
        {
            MainWindow.Toggle();
            return;
        }

        if (TryHandleHelpCommand(args))
        {
            return;
        }

        if (TryHandleStatusCommand(args))
        {
            return;
        }

        if (TryHandleSettingsCommand(args))
        {
            return;
        }

        if (TryHandleModelsCommand(args))
        {
            return;
        }

        if (TryHandleListenCommand(args))
        {
            return;
        }

        if (MainWindow.TryHandleModeCommand(args))
        {
            MainWindow.IsOpen = true;
            return;
        }

        if (TryParseReplyAssistCommand(args, out var target, out var instruction))
        {
            MainWindow.SubmitFromCommand(
                string.IsNullOrWhiteSpace(instruction) ? T("Reply naturally to the recent FFXIV conversation.", "直近のFF14会話に自然に返事して") : instruction,
                replyAssist: true,
                replyTarget: target);
            MainWindow.IsOpen = true;
            return;
        }

        MainWindow.SubmitFromCommand(args, replyAssist: false, replyTarget: null);
        MainWindow.IsOpen = true;
    }

    private bool TryHandleHelpCommand(string args)
    {
        if (!args.Equals("help", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var line in GetHelpLines())
        {
            ChatGui.Print(line, "AI", AiNoticeTagColor);
        }

        return true;
    }

    private bool TryHandleStatusCommand(string args)
    {
        if (!args.Equals("status", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var status = MainWindow.GetStatus();
        var mode = status.Mode switch
        {
            TalkMode.Private => T("Private", "プライベート"),
            TalkMode.PublicAssist => T("Public reply assist", "パブリック返信アシスト"),
            _ => T("DIRECT POSTING", "ダイレクト投稿"),
        };
        var recipient = status.TargetMode switch
        {
            ReplyTargetMode.Conversation => T("Whole conversation", "会話全体"),
            ReplyTargetMode.Party => T("Party", "パーティ全体"),
            ReplyTargetMode.CurrentTarget => string.IsNullOrWhiteSpace(status.ReplyTarget)
                ? T("Current target: none", "現在のターゲット：なし")
                : T("Current target: ", "現在のターゲット：") + status.ReplyTarget,
            _ => string.IsNullOrWhiteSpace(status.ReplyTarget)
                ? T("Named player: none", "相手を指定：なし")
                : T("Named player: ", "相手を指定：") + status.ReplyTarget,
        };
        var modeLine = T("Mode: ", "モード：") + mode;
        if (status.Mode == TalkMode.PublicDirect)
            ChatGui.PrintError(modeLine, "AI", AiNoticeTagColor);
        else
            ChatGui.Print(modeLine, "AI", AiNoticeTagColor);

        if (status.Mode != TalkMode.Private)
            ChatGui.Print(
                T("Recipient: ", "返信先：") + recipient + T(" / Channel: ", " / 投稿先：") + status.PostChannel,
                "AI",
                AiNoticeTagColor);

        var autoResponse = status.Mode == TalkMode.Private
            ? !Configuration.EnablePersonalAutoResponseByChatLines
                ? T("Personal auto response: disabled", "プライベート自動応答：無効")
                : T($"Personal auto response: enabled / after {Configuration.GetClampedPersonalAutoResponseChatLineThreshold()} messages",
                    $"プライベート自動応答：有効 / {Configuration.GetClampedPersonalAutoResponseChatLineThreshold()} メッセージ後に自動生成")
            : !Configuration.EnableAutoResponseByChatLines
                ? T("Auto response: disabled", "自動応答：無効")
                : Configuration.PublicAutoResponseTrigger == PublicAutoResponseTrigger.RecipientSpeech
                    ? T("Auto response: enabled / after recipient speaks", "自動応答：有効 / 返信相手の発言後に自動生成")
                    : MainWindow.IsTellReply
                        ? T($"Auto response: enabled / after {Configuration.GetClampedAutoResponseChatLineThreshold()} messages from the Tell recipient",
                            $"自動応答：有効 / Tell相手の発言{Configuration.GetClampedAutoResponseChatLineThreshold()}件後に自動生成")
                        : T($"Auto response: enabled / after {Configuration.GetClampedAutoResponseChatLineThreshold()} messages",
                            $"自動応答：有効 / {Configuration.GetClampedAutoResponseChatLineThreshold()} メッセージ後に自動生成");
        ChatGui.Print(autoResponse, "AI", AiNoticeTagColor);
        return true;
    }

    private bool TryHandleSettingsCommand(string args)
    {
        if (!args.Equals("config", System.StringComparison.OrdinalIgnoreCase) &&
            !args.Equals("settings", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ConfigWindow.IsOpen = true;
        return true;
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void OpenConfigUi() => ConfigWindow.IsOpen = true;
    public void OpenMainUi() => MainWindow.IsOpen = true;
    public void OpenModelSettings() => ConfigWindow.OpenModelSettingsPage();
    public void DrawModelSettingsPanel() => ModelSettingsWindow.DrawEmbedded();
    public void DrawModelSetupInstructionsPanel() => ModelSettingsWindow.DrawSetupInstructions();
    public void DrawPublicRecipientPanel() => MainWindow.DrawRecipientSelection();

    private void OnLogin()
    {
        ResetSessionSafetyState(saveConfiguration: true);
    }

    private void OnLogout(int type, int code) => ResetSessionSafetyState(saveConfiguration: false);
    private void OnFrameworkUpdate(IFramework framework)
    {
        MainWindow.Update();
        ModelSettingsWindow.Update();
    }

    private void ResetSessionSafetyState(bool saveConfiguration)
    {
        MainWindow.ResetToPersonalMode();
        ChatHistory.Clear();
        ResetAutoResponseCounter();

        if (!Configuration.PostReplyAssistToGameChat)
        {
            return;
        }

        Configuration.PostReplyAssistToGameChat = false;
        if (saveConfiguration)
        {
            Configuration.Save();
        }
    }

    private bool TryHandleModelsCommand(string args)
    {
        if (!args.Equals("models", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        OpenModelSettings();
        return true;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        MainWindow.Update();
        if (!ClientState.IsLoggedIn) return;
        var channel = message.LogKind.ToString();
        var playerPayload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        var sender = playerPayload?.PlayerName ?? message.Sender.TextValue.Trim();
        var senderAddress = playerPayload != null &&
            ChatSafety.TryNormalizeTellRecipient(
                $"{playerPayload.PlayerName}@{playerPayload.World.Value.Name}", out _, out var normalizedAddress)
                ? normalizedAddress
                : "";
        var body = message.Message.TextValue.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var isLocalPlayerSay = message.LogKind == XivChatType.Say && IsLocalPlayer(sender);
        var shouldCapture = ShouldCaptureChatChannel(message.LogKind);
        if (!shouldCapture && !(Configuration.ReplyToOwnSayMessages && isLocalPlayerSay))
        {
            return;
        }

        if (shouldCapture)
        {
            ChatHistory.AddGameChatLine(
                channel, sender, body, Configuration.GetClampedGameChatLineLimit(), senderAddress: senderAddress);
        }
        if (isLocalPlayerSay && !MainWindow.IsPartyReply)
        {
            ResetAutoResponseCounter();
        }

        if (Configuration.ReplyToOwnSayMessages && isLocalPlayerSay)
        {
            MainWindow.SubmitFromAutoTrigger(
                body,
                $"[own Say] {body}",
                replyAssist: false,
                replyTarget: null,
                printToChat: true);
            MainWindow.IsOpen = true;
            return;
        }

        if (IsLocalPlayer(sender) || message.LogKind == XivChatType.TellOutgoing)
        {
            return;
        }

        TryTriggerPersonalAutoResponse();
        TryTriggerPublicAutoResponse(sender, senderAddress, channel, body);
    }

    private void TryTriggerPersonalAutoResponse()
    {
        if (MainWindow.IsPublicMode || !Configuration.EnablePersonalAutoResponseByChatLines)
        {
            return;
        }

        personalAutoResponseCapturedMessageCount++;
        var threshold = Configuration.GetClampedPersonalAutoResponseChatLineThreshold();
        if (personalAutoResponseCapturedMessageCount < threshold)
        {
            return;
        }

        var triggerReason = $"[{threshold} captured messages] Personal auto response from game chat context.";
        if (MainWindow.SubmitFromAutoTrigger(
                T("Review the recent FFXIV conversation and reply naturally to the user.", "直近のFF14会話を見て、ユーザー本人に自然に返事して"),
                triggerReason,
                replyAssist: false,
                replyTarget: null))
        {
            personalAutoResponseCapturedMessageCount = 0;
        }
    }

    private void TryTriggerPublicAutoResponse(string sender, string senderAddress, string channel, string body)
    {
        if (!MainWindow.IsPublicMode || !Configuration.EnableAutoResponseByChatLines)
        {
            return;
        }

        if (MainWindow.IsTellReply && !MainWindow.IsPublicAutoResponseRecipient(sender, senderAddress, channel))
        {
            return;
        }

        if (Configuration.PublicAutoResponseTrigger == PublicAutoResponseTrigger.RecipientSpeech)
        {
            if (!MainWindow.IsPublicAutoResponseRecipient(sender, senderAddress, channel)) return;
            var triggerReason = $"[recipient speech] {sender}: {body}";
            if (MainWindow.SubmitFromAutoTrigger(
                    T("Reply naturally to the recent FFXIV conversation.", "直近のFF14会話に自然に返事して"),
                    triggerReason,
                    replyAssist: true,
                    replyTarget: null))
            {
                publicAutoResponseCapturedMessageCount = 0;
            }
            return;
        }

        if (MainWindow.IsPartyReply && !ChatSafety.IsPartyChannel(channel)) return;

        publicAutoResponseCapturedMessageCount++;
        var threshold = Configuration.GetClampedAutoResponseChatLineThreshold();
        if (publicAutoResponseCapturedMessageCount >= threshold)
        {
            var triggerReason = $"[{threshold} captured messages] Auto response from game chat context.";
            if (MainWindow.SubmitFromAutoTrigger(
                    T("Reply naturally to the recent FFXIV conversation.", "直近のFF14会話に自然に返事して"),
                    triggerReason,
                    replyAssist: true,
                    replyTarget: null))
            {
                publicAutoResponseCapturedMessageCount = 0;
            }
        }
    }

    public void ResetAutoResponseCounter()
    {
        personalAutoResponseCapturedMessageCount = 0;
        publicAutoResponseCapturedMessageCount = 0;
    }

    private bool TryHandleListenCommand(string args)
    {
        if (!args.Equals("listen", System.StringComparison.OrdinalIgnoreCase) &&
            !args.StartsWith("listen ", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = args.Length == "listen".Length ? "" : args["listen".Length..].Trim();
        if (rest.Equals("on", System.StringComparison.OrdinalIgnoreCase))
        {
            Configuration.ReplyToOwnSayMessages = true;
            ConfigWindow.SyncListenSetting();
            Configuration.Save();
            ChatGui.Print(T("Listen enabled for your Say messages.", "自分のSay発言へのリッスンを有効にしました。"), "AI", AiNoticeTagColor);
            return true;
        }

        if (rest.Equals("off", System.StringComparison.OrdinalIgnoreCase))
        {
            Configuration.ReplyToOwnSayMessages = false;
            ConfigWindow.SyncListenSetting();
            Configuration.Save();
            ChatGui.Print(T("Listen disabled.", "リッスンを無効にしました。"), "AI", AiNoticeTagColor);
            return true;
        }

        ChatGui.Print(T("Usage: /airp listen on | /airp listen off", "使い方: /airp listen on | /airp listen off"), "AI", AiNoticeTagColor);
        return true;
    }

    private string[] GetHelpLines() =>
    [
        T("AIRoleplay commands:", "AIRoleplay コマンド:"),
        T("/airp - Open AIRoleplay.", "/airp - AIRoleplayを開く。"),
        T("/airp config - Open settings.", "/airp config - 設定を開く。"),
        T("/airp settings - Open settings.", "/airp settings - 設定を開く。"),
        T("/airp models - Open LLM model settings.", "/airp models - LLMモデル設定ページを開く。"),
        T("/airp [message] - Generate in the selected mode.", "/airp [message] - 現在のモードで生成する。"),
        T("/airp private - Switch to private AI conversation.", "/airp private - プライベート会話へ切り替える。"),
        T("/airp public - Switch to public reply assist.", "/airp public - パブリック返信アシストへ切り替える。"),
        T("/airp direct - Switch to direct game chat posting.", "/airp direct - ダイレクト投稿へ切り替える。"),
        T("/airp status - Show the current mode and auto response settings.", "/airp status - 現在のモードと自動応答設定を表示する。"),
        T("/airp r [name] [instruction] - Switch to public reply assist and generate.", "/airp r [name] [instruction] - パブリック返信アシストへ切り替えて生成する。"),
        T("/airp listen on - Reply to your Say messages in personal mode automatically.", "/airp listen on - 自分のSay発言へパーソナルモードで自動返信する。"),
        T("/airp listen off - Stop listening to your Say messages.", "/airp listen off - 自分のSay発言のリッスンを止める。"),
        T("/airp help - Show this command list.", "/airp help - このコマンド一覧を表示する。"),
        "/airp private | public | direct",
        "/airp target current | party | none | \"Firstname Lastname@World\"",
        "/airp channel say | party | fc | tell",
    ];

    private string T(string english, string japanese) => UiText.T(Configuration.Language, english, japanese);

    private static bool IsLocalPlayer(string sender)
    {
        var localName = ObjectTable.LocalPlayer?.Name.TextValue;
        return !string.IsNullOrWhiteSpace(localName)
            && string.Equals(sender, localName, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseReplyAssistCommand(string args, out string? target, out string instruction)
    {
        target = null;
        instruction = "";

        if (!args.Equals("r", System.StringComparison.OrdinalIgnoreCase) &&
            !args.StartsWith("r ", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = args.Length == 1 ? "" : args[1..].Trim();
        if (string.IsNullOrWhiteSpace(rest))
        {
            return true;
        }

        if (rest[0] == '"')
        {
            var endQuote = rest.IndexOf('"', 1);
            if (endQuote > 1)
            {
                target = rest[1..endQuote].Trim();
                instruction = rest[(endQuote + 1)..].Trim();
                return true;
            }
        }

        var firstSpace = rest.IndexOf(' ');
        if (firstSpace < 0)
        {
            target = rest;
            return true;
        }

        target = rest[..firstSpace].Trim();
        instruction = rest[(firstSpace + 1)..].Trim();
        return true;
    }

    public bool ShouldCaptureChatChannel(XivChatType chatType)
    {
        if (MainWindow.IsPartyReply && (chatType is XivChatType.Party or XivChatType.CrossParty))
            return true;
        return chatType is
            XivChatType.Say
                ? Configuration.CaptureSay :
            chatType is
            XivChatType.Shout or
            XivChatType.Yell
                ? Configuration.CaptureShoutYell :
            chatType is
            XivChatType.TellIncoming or
            XivChatType.TellOutgoing
                ? Configuration.CaptureTell :
            chatType is
            XivChatType.Party or
            XivChatType.CrossParty or
            XivChatType.Alliance
                ? Configuration.CaptureParty :
            chatType is
            XivChatType.FreeCompany
                ? Configuration.CaptureFreeCompany :
            chatType is
            XivChatType.Ls1 or
            XivChatType.Ls2 or
            XivChatType.Ls3 or
            XivChatType.Ls4 or
            XivChatType.Ls5 or
            XivChatType.Ls6 or
            XivChatType.Ls7 or
            XivChatType.Ls8 or
            XivChatType.CrossLinkShell1 or
            XivChatType.CrossLinkShell2 or
            XivChatType.CrossLinkShell3 or
            XivChatType.CrossLinkShell4 or
            XivChatType.CrossLinkShell5 or
            XivChatType.CrossLinkShell6 or
            XivChatType.CrossLinkShell7 or
            XivChatType.CrossLinkShell8
                ? Configuration.CaptureLinkshell :
            chatType is
            XivChatType.NoviceNetwork
                ? Configuration.CaptureNoviceNetwork :
            chatType is
            XivChatType.PvPTeam
                ? Configuration.CapturePvpTeam :
            chatType is
            XivChatType.NPCDialogue or
            XivChatType.NPCDialogueAnnouncements
                ? Configuration.CaptureNpcDialogue :
            chatType is
            XivChatType.CustomEmote or
            XivChatType.StandardEmote
                ? Configuration.CaptureEmote :
            chatType is
            XivChatType.RandomNumber
                ? Configuration.CaptureRandomNumber :
            chatType is
            XivChatType.Alarm
                ? Configuration.CaptureAlarm :
            chatType is
            XivChatType.Orchestrion
                ? Configuration.CaptureOrchestrion :
            false;
    }
}
