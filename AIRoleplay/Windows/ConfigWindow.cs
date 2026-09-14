using System;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;

namespace AIRoleplay.Windows;

public class ConfigWindow : Window, IDisposable
{
    private const ushort AiNoticeTagColor = 43;
    private const float NavigationWidth = 185.0f;
    private const float CharacterNameInputWidth = 320.0f;
    private const float CharacterPromptInputWidth = 520.0f;
    private const float CharacterPromptInputHeight = 180.0f;
    private const float SmallNumberInputWidth = 160.0f;

    private enum ConfigPage
    {
        General,
        Models,
        ModelSetup,
        PersonalMode,
        PublicMode,
    }

    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly ChatHistory chatHistory;
    private readonly FileDialogManager profileDialogs = new();
    private string draftCharacterPrompt = "";
    private string draftAssistantDisplayName = "";
    private string draftPublicCharacterPrompt = "";
    private string draftPublicAssistantDisplayName = "";
    private bool draftShowAiResponseInLocalChat;
    private bool draftReplyToOwnSayMessages;
    private bool draftEnablePersonalAutoResponseByChatLines;
    private bool draftEnablePrivateConversationSummary;
    private int draftPersonalAutoResponseChatLineThreshold = Configuration.DefaultAutoResponseChatLineThreshold;
    private bool draftEnableAutoResponseByChatLines;
    private bool draftEnablePublicConversationSummary;
    private PublicAutoResponseTrigger draftPublicAutoResponseTrigger;
    private int draftAutoResponseChatLineThreshold = Configuration.DefaultAutoResponseChatLineThreshold;
    private int draftGameChatLineLimit = Configuration.DefaultGameChatLineLimit;
    private bool draftCaptureSay;
    private bool draftCaptureShoutYell;
    private bool draftCaptureTell;
    private bool draftCaptureParty;
    private bool draftCaptureFreeCompany;
    private bool draftCaptureLinkshell;
    private bool draftCaptureNoviceNetwork;
    private bool draftCapturePvpTeam;
    private bool draftCaptureNpcDialogue;
    private bool draftCaptureEmote;
    private bool draftCaptureRandomNumber;
    private bool draftCaptureAlarm;
    private bool draftCaptureOrchestrion;
    private UiLanguage draftLanguage = UiLanguage.English;
    private string statusText = "";
    private string? loadedPublicProfilePath;
    private CharacterProfileFileError publicProfileLoadError;
    private ConfigPage currentPage = ConfigPage.General;

    public ConfigWindow(Plugin plugin) : base("AIRoleplay Settings###AIRoleplaySettings")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 390),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        this.plugin = plugin;
        configuration = plugin.Configuration;
        chatHistory = plugin.ChatHistory;
        LoadDraftFromConfiguration(loadPublicProfile: false);
    }

    public override void OnOpen() => LoadDraftFromConfiguration();
    public void SyncListenSetting() => draftReplyToOwnSayMessages = configuration.ReplyToOwnSayMessages;
    public void DrawDialogs() => profileDialogs.Draw();

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawNavigationPane();
        ImGui.SameLine();
        DrawContentPane();
    }

    public void OpenModelSettingsPage()
    {
        currentPage = ConfigPage.Models;
        IsOpen = true;
    }

    private void DrawNavigationPane()
    {
        ImGui.BeginChild("##AIRoleplaySettingsNavigation", new Vector2(NavigationWidth, 0), true);
        if (ImGui.Button(T("Open main window", "メインウィンドウを開く")))
            plugin.OpenMainUi();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawNavigationItem(ConfigPage.General, T("UI Language", "UI言語設定"));
        DrawNavigationItem(ConfigPage.Models, T("LLM Models", "LLMモデル設定"));
        DrawNavigationItem(ConfigPage.PersonalMode, T("Private Mode", "プライベートモード設定"));
        DrawNavigationItem(ConfigPage.PublicMode, T("Public Mode", "パブリックモード設定"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawNavigationItem(ConfigPage.ModelSetup, T("LLM API Key Setup", "LLM APIキー登録方法"));
        ImGui.EndChild();
    }

    private void DrawNavigationItem(ConfigPage page, string label)
    {
        if (ImGui.Selectable(label, currentPage == page))
        {
            currentPage = page;
            statusText = "";
        }
    }

    private void DrawContentPane()
    {
        ImGui.BeginChild("##AIRoleplaySettingsContent", new Vector2(0, 0), false);
        switch (currentPage)
        {
            case ConfigPage.General:
                DrawGeneralSettings();
                DrawDraftButtons();
                break;
            case ConfigPage.PersonalMode:
                DrawPersonalModeSettings();
                DrawDraftButtons();
                break;
            case ConfigPage.PublicMode:
                DrawPublicModeSettings();
                DrawDraftButtons();
                break;
            case ConfigPage.Models:
                plugin.DrawModelSettingsPanel();
                break;
            case ConfigPage.ModelSetup:
                plugin.DrawModelSetupInstructionsPanel();
                break;
        }

        ImGui.EndChild();
    }

    private void DrawDraftButtons()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(T("Save", "保存")))
        {
            var error = SaveDraft();
            statusText = error == CharacterProfileFileError.None
                ? T("Saved.", "保存しました。")
                : GetProfileFileErrorMessage(error);
        }

        ImGui.SameLine();
        if (ImGui.Button(T("Cancel", "キャンセル")))
        {
            LoadDraftFromConfiguration();
            statusText = T("Canceled.", "キャンセルしました。");
        }

        if (!string.IsNullOrEmpty(statusText))
        {
            ImGui.SameLine();
            ImGui.TextDisabled(statusText);
        }
    }

    private void DrawGeneralSettings()
    {
        ImGui.Text(T("UI Language", "UI言語設定"));
        ImGui.Spacing();
        DrawLanguageSettings();
    }

    private void DrawPersonalModeSettings()
    {
        ImGui.Text(T("Private Mode Settings", "プライベートモード設定"));
        ImGui.TextWrapped(T(
            "Configure how the AI replies directly to you, including conversations in the AIRoleplay window and automatic replies to your own Say messages.",
            "ユーザー本人へ返信するAIの設定です。AIRoleplayウィンドウでの会話や、自分のSay発言への自動返信に使われます。"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawCharacterSettings(
            T("Personal AI Character", "プライベート用AIキャラクター"),
            "Personal",
            ref draftAssistantDisplayName,
            ref draftCharacterPrompt);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawConversationSummarySettings(ref draftEnablePrivateConversationSummary, isPublic: false, "Private");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawAiResponseSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawListenModeSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawChatCaptureSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawAutoResponseSettings(
            ref draftEnablePersonalAutoResponseByChatLines,
            ref draftPersonalAutoResponseChatLineThreshold,
            T("Enable personal auto response", "プライベート自動応答を有効にする"),
            "Personal");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        plugin.LogSettings.Draw(isPublic: false);
    }

    private void DrawPublicModeSettings()
    {
        RefreshPublicProfileDraftForCurrentCharacter();
        ImGui.Text(T("Public Mode Settings", "パブリックモード設定"));
        ImGui.TextWrapped(T(
            "Generate speech as your player character for other players. Reply assist fills the game chat input for you to review and send; Direct posting sends generated text to the selected channel without a review or confirmation step.",
            "プレイヤー本人として、他プレイヤーへ向けた発言を生成する設定です。返信アシストでは生成文をゲームのチャット入力欄に反映し、確認・編集してから自分で送信します。ダイレクト投稿では、生成文を確認する手順を挟まず、指定したチャンネルへそのまま送信します。"));
        ImGui.Spacing();
        plugin.DrawPublicRecipientPanel();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        var profilePath = plugin.CharacterProfiles.GetCurrentProfilePath();
        ImGui.BeginDisabled(profilePath == null);
        DrawCharacterSettings(
            T("Player Character", "プレイヤー本人の設定"),
            "Public",
            ref draftPublicAssistantDisplayName,
            ref draftPublicCharacterPrompt,
            showName: false);
        ImGui.EndDisabled();
        DrawPublicProfileFileSettings(profilePath);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawConversationSummarySettings(ref draftEnablePublicConversationSummary, isPublic: true, "Public");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawChatCaptureSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawPublicAutoResponseSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        plugin.LogSettings.Draw(isPublic: true);
    }

    private void DrawLanguageSettings()
    {
        ImGui.Text(T("UI Language", "UI言語"));
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##AIRoleplayLanguage", UiText.LanguageName(draftLanguage)))
        {
            foreach (var language in Enum.GetValues<UiLanguage>())
            {
                if (ImGui.Selectable(UiText.LanguageName(language), draftLanguage == language))
                {
                    draftLanguage = language;
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawCharacterSettings(
        string heading,
        string idSuffix,
        ref string draftDisplayName,
        ref string draftPrompt,
        bool showName = true)
    {
        ImGui.Text(heading);

        if (showName)
        {
            ImGui.Text(T("AI Character Name", "AIキャラクター名"));
            ImGui.SetNextItemWidth(GetContentItemWidth(CharacterNameInputWidth));
            if (ImGui.InputText($"##AIRoleplay{idSuffix}AssistantDisplayName", ref draftDisplayName, 201))
                draftDisplayName = Limit(draftDisplayName, Configuration.MaxAssistantDisplayNameLength);
            ImGui.TextDisabled($"{draftDisplayName.Length}/{Configuration.MaxAssistantDisplayNameLength}");
        }
        ImGui.Text(showName ? T("AI Character Profile", "AIキャラクター設定") : T("Player personality and speech", "プレイヤー本人の人物像・口調"));
        var promptWidth = GetContentItemWidth(CharacterPromptInputWidth);
        var placeholder = showName
            ? T(
                "Example: You are a female adventurer traveling with the user. You are calm and kind, with an occasional playful joke. Treat the user as a close friend, take the recent conversation and in-game situation into account, and respond in natural English without breaking the setting. Keep each reply to about 200 characters.",
                "（例）あなたはユーザーと一緒に旅をしている女性の冒険者仲間です。穏やかで優しく、ときどき冗談も言います。ユーザーとは親しい友人として接し、直近の会話やゲーム内の状況を踏まえて、世界観を壊さない自然な日本語で返答してください。返答は長くなりすぎないよう、原則200文字程度にまとめてください。")
            : T(
                "Example: You are a seasoned adventurer who is calm and unshaken by most danger. You care deeply for your companions, cannot ignore anyone in need, and usually accept requests in the end. Choose your words carefully and occasionally make a witty joke or light sarcastic remark. Respond naturally based on the recent conversation and in-game situation. Keep each reply within 200 characters.",
                "（例）落ち着いた歴戦の冒険者で、多少の危機には動じない。仲間思いで、困っている者を放っておけず、頼まれると結局引き受ける。状況に応じて言葉を選びながら、ときどき気の利いた冗談や軽い皮肉を交えて話す。直近の会話やゲーム内の状況を踏まえて自然に返答する。返答は原則200文字以内。");
        var promptPosition = ImGui.GetCursorScreenPos();
        if (string.IsNullOrWhiteSpace(draftPrompt))
        {
            var promptSize = new Vector2(promptWidth, CharacterPromptInputHeight);
            var frameColor = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
            ImGui.PushStyleColor(ImGuiCol.Button, frameColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, frameColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, frameColor);
            var useExample = ImGui.Button($"##AIRoleplay{idSuffix}CharacterPromptExample", promptSize);
            ImGui.PopStyleColor(3);

            if (!useExample)
            {
                var padding = ImGui.GetStyle().FramePadding;
                ImGui.GetWindowDrawList().AddText(
                    ImGui.GetFont(),
                    ImGui.GetFontSize(),
                    promptPosition + padding,
                    ImGui.GetColorU32(ImGuiCol.TextDisabled),
                    placeholder,
                    promptWidth - padding.X * 2);
                ImGui.TextDisabled($"{draftPrompt.Length}/{Configuration.MaxCharacterPromptLength}");
                return;
            }

            draftPrompt = placeholder;
            ImGui.SetCursorScreenPos(promptPosition);
            ImGui.SetKeyboardFocusHere();
        }

        if (ImGui.InputTextMultiline(
                $"##AIRoleplay{idSuffix}CharacterPrompt",
                ref draftPrompt,
                2001,
                new Vector2(promptWidth, CharacterPromptInputHeight)))
        {
            draftPrompt = Limit(draftPrompt, Configuration.MaxCharacterPromptLength);
        }

        ImGui.TextDisabled($"{draftPrompt.Length}/{Configuration.MaxCharacterPromptLength}");
    }

    private void DrawAiResponseSettings()
    {
        ImGui.Text(T("AI Response", "AI応答"));
        ImGui.Checkbox(T("Show AI response in local chat", "AI応答をローカルチャットに表示"), ref draftShowAiResponseInLocalChat);
    }

    private void DrawConversationSummarySettings(ref bool enabled, bool isPublic, string idSuffix)
    {
        ImGui.Text(T("Long-term conversation context", "長期的な会話の文脈"));
        ImGui.Checkbox(
            T("Use an AI-generated conversation summary", "AIが生成する会話要約を使用する") + $"##AIRoleplay{idSuffix}ConversationSummary",
            ref enabled);
        ImGui.TextWrapped(T(
            "Each generation returns the reply and an updated hidden summary in one JSON response. Only the reply is shown. This increases output token usage.",
            "生成ごとに、発言と更新後の内部要約を1つのJSON応答で受け取ります。表示されるのは発言だけです。出力トークン使用量は増加します。"));
        if (isPublic)
            ImGui.TextDisabled(T(
                "Public summaries are separated by channel and recipient. Tell summaries are kept separately for each Name@World.",
                "パブリック要約は投稿先と返信相手ごとに分離されます。TellはName@Worldごとに別の要約になります。"));
        if (ImGui.Button(T("Reset hidden summary", "内部要約をリセット") + $"##AIRoleplay{idSuffix}ConversationSummaryReset"))
        {
            chatHistory.ClearConversationSummaries(isPublic);
            statusText = T("Hidden conversation summary reset.", "内部会話要約をリセットしました。");
        }
    }

    private void DrawPublicProfileFileSettings(string? profilePath)
    {
        ImGui.Text(T("Character profile file", "人物像ファイル"));
        if (profilePath == null)
        {
            ImGui.TextDisabled(T(
                "Log in to edit a player character profile.",
                "プレイヤー本人の人物像を編集するにはログインしてください。"));
            return;
        }

        ImGui.TextWrapped(profilePath);
        if (publicProfileLoadError == CharacterProfileFileError.NotFound)
        {
            ImGui.TextDisabled(T(
                "No profile file exists yet. Save to create it.",
                "人物像ファイルはまだありません。保存すると作成されます。"));
        }
        else if (publicProfileLoadError != CharacterProfileFileError.None)
        {
            ImGui.TextDisabled(GetProfileFileErrorMessage(publicProfileLoadError));
        }

        if (ImGui.Button(T("Load from file...", "ファイルから読み込む...")))
        {
            var characterDirectory = Path.GetDirectoryName(profilePath)!;
            var startDirectory = Directory.Exists(characterDirectory)
                ? characterDirectory
                : Directory.Exists(plugin.CharacterProfiles.CharactersDirectory)
                    ? plugin.CharacterProfiles.CharactersDirectory
                    : Plugin.PluginInterface.ConfigDirectory.FullName;
            profileDialogs.OpenFileDialog(
                T("Load character profile", "人物像ファイルを読み込む"),
                ".txt,.md",
                (ok, paths) =>
                {
                    if (!ok || paths.Count == 0) return;
                    if (plugin.CharacterProfiles.TryReadFile(paths[0], out var profile, out var error))
                    {
                        draftPublicCharacterPrompt = profile;
                        publicProfileLoadError = CharacterProfileFileError.None;
                        statusText = T(
                            "Loaded into the editor. Save to store it for this character.",
                            "編集欄へ読み込みました。保存するとこのキャラクター用に保存されます。");
                    }
                    else
                    {
                        statusText = GetProfileFileErrorMessage(error);
                    }
                },
                1,
                startDirectory,
                false);
        }

        ImGui.TextDisabled(T(
            "UTF-8 .txt/.md files are supported. Save writes the editor contents to this character's profile.txt.",
            "UTF-8の.txt／.mdに対応しています。保存すると編集欄の内容をこのキャラクターのprofile.txtへ書き込みます。"));
    }

    private void DrawListenModeSettings()
    {
        ImGui.Checkbox(T("Reply to my Say messages", "自分のSay発言に返信する"), ref draftReplyToOwnSayMessages);
    }

    private void DrawChatCaptureSettings()
    {
        ImGui.Text(T("Game Chat Context", "ゲームチャット文脈"));

        ImGui.SetNextItemWidth(GetContentItemWidth(SmallNumberInputWidth));
        if (ImGui.InputInt(T("Max timeline entries sent to AI##AIRoleplayCapturedLines", "AIへ送る最大履歴件数##AIRoleplayCapturedLines"), ref draftGameChatLineLimit))
        {
            draftGameChatLineLimit = Math.Clamp(
                draftGameChatLineLimit,
                Configuration.MinGameChatLineLimit,
                Configuration.MaxGameChatLineLimit);
        }

        ImGui.TextDisabled(T(
            $"{Configuration.MinGameChatLineLimit}-{Configuration.MaxGameChatLineLimit} recent game, user, and AI entries",
            $"ゲームログ・ユーザー発言・AI返答を合わせた直近 {Configuration.MinGameChatLineLimit}-{Configuration.MaxGameChatLineLimit} 件"));

        ImGui.Text(T("Chat channels sent to AI", "AIへ送るチャットチャンネル"));
        ImGui.Checkbox(T("Say", "Say"), ref draftCaptureSay);
        ImGui.SameLine();
        ImGui.Checkbox(T("Shout/Yell", "Shout/Yell"), ref draftCaptureShoutYell);
        ImGui.SameLine();
        ImGui.Checkbox(T("Tell", "Tell"), ref draftCaptureTell);

        ImGui.Checkbox(T("Party/Alliance", "Party/Alliance"), ref draftCaptureParty);
        ImGui.SameLine();
        ImGui.Checkbox(T("Free Company", "Free Company"), ref draftCaptureFreeCompany);

        ImGui.Checkbox(T("Linkshell/CWLS", "Linkshell/CWLS"), ref draftCaptureLinkshell);
        ImGui.SameLine();
        ImGui.Checkbox(T("Novice Network", "Novice Network"), ref draftCaptureNoviceNetwork);
        ImGui.SameLine();
        ImGui.Checkbox(T("PvP Team", "PvP Team"), ref draftCapturePvpTeam);

        ImGui.Checkbox(T("NPC Dialogue", "NPC会話"), ref draftCaptureNpcDialogue);
        ImGui.SameLine();
        ImGui.Checkbox(T("Emote", "エモート"), ref draftCaptureEmote);

        ImGui.Checkbox(T("Random Number", "ランダム番号"), ref draftCaptureRandomNumber);
        ImGui.SameLine();
        ImGui.Checkbox(T("Alarm", "アラーム"), ref draftCaptureAlarm);
        ImGui.SameLine();
        ImGui.Checkbox(T("Orchestrion", "オーケストリオン"), ref draftCaptureOrchestrion);

    }

    private void DrawAutoResponseSettings(
        ref bool draftEnabled,
        ref int draftThreshold,
        string enableLabel,
        string idSuffix)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.78f, 0.25f, 1.0f));
        ImGui.Text(T("! Auto Response", "！自動応答"));
        ImGui.TextWrapped(T(
            "When enabled, AIRoleplay automatically sends an LLM request and replies each time the number of chat logs specified below has accumulated. API usage and costs may increase. Enable this only when you need background auto responses.",
            "有効にすると、下記で指定した件数分のチャットログがたまるたびに、AIRoleplayが自動でLLMリクエストを送信して返事します。API使用量や費用が増える可能性があります。バックグラウンドでの自動応答が必要な場合のみ有効にしてください。"));
        ImGui.PopStyleColor();

        ImGui.Checkbox(enableLabel, ref draftEnabled);

        ImGui.SetNextItemWidth(GetContentItemWidth(SmallNumberInputWidth));
        if (ImGui.InputInt(T(
                $"Auto-generate after captured messages##AIRoleplay{idSuffix}AutoResponseLines",
                $"取得メッセージ数で自動生成##AIRoleplay{idSuffix}AutoResponseLines"), ref draftThreshold))
        {
            draftThreshold = Math.Clamp(
                draftThreshold,
                Configuration.MinAutoResponseChatLineThreshold,
                Configuration.MaxAutoResponseChatLineThreshold);
        }

        ImGui.TextDisabled(T(
            $"{Configuration.MinAutoResponseChatLineThreshold}-{Configuration.MaxAutoResponseChatLineThreshold} captured messages",
            $"{Configuration.MinAutoResponseChatLineThreshold}-{Configuration.MaxAutoResponseChatLineThreshold} 件の取得メッセージ"));
        ImGui.TextDisabled(T(
            "Counter resets after any AI response, manual send, or local player Say message.",
            "AI応答、手動送信、または自分のSay発言後にカウンターはリセットされます。"));
    }

    private void DrawPublicAutoResponseSettings()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.78f, 0.25f, 1.0f));
        ImGui.Text(T("! Auto Response", "！自動応答"));
        ImGui.TextWrapped(T(
            "When enabled, AIRoleplay automatically creates a reply from either the selected trigger. API usage and costs may increase.",
            "有効にすると、選択したトリガーでAIRoleplayが自動的に返信を生成します。API使用量や費用が増える可能性があります。"));
        ImGui.PopStyleColor();

        ImGui.Checkbox(T("Enable public auto response", "パブリック自動応答を有効にする"), ref draftEnableAutoResponseByChatLines);
        ImGui.Text(T("Auto response trigger", "自動応答トリガー"));
        if (ImGui.RadioButton("##AIRoleplayPublicAutoResponseCount",
                draftPublicAutoResponseTrigger == PublicAutoResponseTrigger.CapturedMessageCount))
            draftPublicAutoResponseTrigger = PublicAutoResponseTrigger.CapturedMessageCount;
        ImGui.SameLine();
        ImGui.BeginDisabled(draftPublicAutoResponseTrigger != PublicAutoResponseTrigger.CapturedMessageCount);
        ImGui.SetNextItemWidth(GetContentItemWidth(SmallNumberInputWidth));
        if (ImGui.InputInt("##AIRoleplayPublicAutoResponseLines", ref draftAutoResponseChatLineThreshold))
        {
            draftAutoResponseChatLineThreshold = Math.Clamp(
                draftAutoResponseChatLineThreshold,
                Configuration.MinAutoResponseChatLineThreshold,
                Configuration.MaxAutoResponseChatLineThreshold);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.Text(T("captured messages before auto-generate", "メッセージ後に自動生成"));

        if (ImGui.RadioButton(T("Auto-generate after selected recipient speaks", "返信相手の発言後に自動生成"),
                draftPublicAutoResponseTrigger == PublicAutoResponseTrigger.RecipientSpeech))
            draftPublicAutoResponseTrigger = PublicAutoResponseTrigger.RecipientSpeech;

        if (draftPublicAutoResponseTrigger == PublicAutoResponseTrigger.RecipientSpeech)
        {
            ImGui.TextDisabled(T(
                "Generates when the selected recipient speaks. Whole conversation has no recipient trigger.",
                "選択した返信先が発言すると生成します。会話全体には返信先トリガーはありません。"));
        }
    }

    private void LoadDraftFromConfiguration(bool loadPublicProfile = true)
    {
        draftLanguage = configuration.Language;
        draftCharacterPrompt = Limit(configuration.CharacterPrompt, Configuration.MaxCharacterPromptLength);
        draftAssistantDisplayName = Limit(configuration.AssistantDisplayName, Configuration.MaxAssistantDisplayNameLength);
        if (loadPublicProfile)
        {
            LoadPublicProfileDraft();
        }
        else
        {
            draftPublicCharacterPrompt = "";
            loadedPublicProfilePath = null;
            publicProfileLoadError = CharacterProfileFileError.None;
        }
        draftPublicAssistantDisplayName = Limit(configuration.PublicAssistantDisplayName, Configuration.MaxAssistantDisplayNameLength);
        draftShowAiResponseInLocalChat = configuration.ShowAiResponseInLocalChat;
        draftReplyToOwnSayMessages = configuration.ReplyToOwnSayMessages;
        draftEnablePersonalAutoResponseByChatLines = configuration.EnablePersonalAutoResponseByChatLines;
        draftEnablePrivateConversationSummary = configuration.EnablePrivateConversationSummary;
        draftPersonalAutoResponseChatLineThreshold = configuration.GetClampedPersonalAutoResponseChatLineThreshold();
        draftEnableAutoResponseByChatLines = configuration.EnableAutoResponseByChatLines;
        draftEnablePublicConversationSummary = configuration.EnablePublicConversationSummary;
        draftPublicAutoResponseTrigger = configuration.PublicAutoResponseTrigger;
        draftAutoResponseChatLineThreshold = configuration.GetClampedAutoResponseChatLineThreshold();
        draftGameChatLineLimit = configuration.GetClampedGameChatLineLimit();
        draftCaptureSay = configuration.CaptureSay;
        draftCaptureShoutYell = configuration.CaptureShoutYell;
        draftCaptureTell = configuration.CaptureTell;
        draftCaptureParty = configuration.CaptureParty;
        draftCaptureFreeCompany = configuration.CaptureFreeCompany;
        draftCaptureLinkshell = configuration.CaptureLinkshell;
        draftCaptureNoviceNetwork = configuration.CaptureNoviceNetwork;
        draftCapturePvpTeam = configuration.CapturePvpTeam;
        draftCaptureNpcDialogue = configuration.CaptureNpcDialogue;
        draftCaptureEmote = configuration.CaptureEmote;
        draftCaptureRandomNumber = configuration.CaptureRandomNumber;
        draftCaptureAlarm = configuration.CaptureAlarm;
        draftCaptureOrchestrion = configuration.CaptureOrchestrion;
    }

    private CharacterProfileFileError SaveDraft()
    {
        var wasAutoResponseEnabled = configuration.EnableAutoResponseByChatLines;
        var wasPersonalAutoResponseEnabled = configuration.EnablePersonalAutoResponseByChatLines;

        if (currentPage == ConfigPage.PublicMode && plugin.CharacterProfiles.GetCurrentProfilePath() != null)
        {
            if (!plugin.CharacterProfiles.TryWriteCurrent(draftPublicCharacterPrompt, out var profileError))
                return profileError;
            loadedPublicProfilePath = plugin.CharacterProfiles.GetCurrentProfilePath();
            publicProfileLoadError = CharacterProfileFileError.None;
        }

        configuration.CharacterPrompt = Limit(draftCharacterPrompt, Configuration.MaxCharacterPromptLength);
        configuration.AssistantDisplayName = Limit(draftAssistantDisplayName.Trim(), Configuration.MaxAssistantDisplayNameLength);
        configuration.PublicAssistantDisplayName = Limit(draftPublicAssistantDisplayName.Trim(), Configuration.MaxAssistantDisplayNameLength);
        configuration.ShowAiResponseInLocalChat = draftShowAiResponseInLocalChat;
        configuration.ReplyToOwnSayMessages = draftReplyToOwnSayMessages;
        configuration.EnablePersonalAutoResponseByChatLines = draftEnablePersonalAutoResponseByChatLines;
        configuration.EnablePrivateConversationSummary = draftEnablePrivateConversationSummary;
        configuration.PersonalAutoResponseChatLineThreshold = Math.Clamp(
            draftPersonalAutoResponseChatLineThreshold,
            Configuration.MinAutoResponseChatLineThreshold,
            Configuration.MaxAutoResponseChatLineThreshold);
        configuration.EnableAutoResponseByChatLines = draftEnableAutoResponseByChatLines;
        configuration.EnablePublicConversationSummary = draftEnablePublicConversationSummary;
        configuration.PublicAutoResponseTrigger = draftPublicAutoResponseTrigger;
        configuration.AutoResponseChatLineThreshold = Math.Clamp(
            draftAutoResponseChatLineThreshold,
            Configuration.MinAutoResponseChatLineThreshold,
            Configuration.MaxAutoResponseChatLineThreshold);
        configuration.GameChatLineLimit = Math.Clamp(
            draftGameChatLineLimit,
            Configuration.MinGameChatLineLimit,
            Configuration.MaxGameChatLineLimit);
        configuration.CaptureSay = draftCaptureSay;
        configuration.CaptureShoutYell = draftCaptureShoutYell;
        configuration.CaptureTell = draftCaptureTell;
        configuration.CaptureParty = draftCaptureParty;
        configuration.CaptureFreeCompany = draftCaptureFreeCompany;
        configuration.CaptureLinkshell = draftCaptureLinkshell;
        configuration.CaptureNoviceNetwork = draftCaptureNoviceNetwork;
        configuration.CapturePvpTeam = draftCapturePvpTeam;
        configuration.CaptureNpcDialogue = draftCaptureNpcDialogue;
        configuration.CaptureEmote = draftCaptureEmote;
        configuration.CaptureRandomNumber = draftCaptureRandomNumber;
        configuration.CaptureAlarm = draftCaptureAlarm;
        configuration.CaptureOrchestrion = draftCaptureOrchestrion;
        configuration.Language = draftLanguage;
        configuration.Save();
        chatHistory.SetTimelineLimit(configuration.GetClampedGameChatLineLimit());

        if (!wasPersonalAutoResponseEnabled && configuration.EnablePersonalAutoResponseByChatLines)
        {
            Plugin.ChatGui.Print(
                T("Personal auto response enabled. LLM requests may be sent automatically and increase API usage/costs.",
                    "プライベート自動応答を有効にしました。LLMリクエストが自動送信され、API使用量や費用が増える可能性があります。"),
                "AI",
                AiNoticeTagColor);
        }
        else if (wasPersonalAutoResponseEnabled && !configuration.EnablePersonalAutoResponseByChatLines)
        {
            Plugin.ChatGui.Print(T("Personal auto response disabled.", "プライベート自動応答を無効にしました。"), "AI", AiNoticeTagColor);
        }

        if (!wasAutoResponseEnabled && configuration.EnableAutoResponseByChatLines)
        {
            Plugin.ChatGui.Print(
                T("Public auto response enabled. LLM requests may be sent automatically and increase API usage/costs.",
                    "パブリック自動応答を有効にしました。LLMリクエストが自動送信され、API使用量や費用が増える可能性があります。"),
                "AI",
                AiNoticeTagColor);
        }
        else if (wasAutoResponseEnabled && !configuration.EnableAutoResponseByChatLines)
        {
            Plugin.ChatGui.Print(T("Public auto response disabled.", "パブリック自動応答を無効にしました。"), "AI", AiNoticeTagColor);
        }

        return CharacterProfileFileError.None;
    }

    private void RefreshPublicProfileDraftForCurrentCharacter()
    {
        var currentPath = plugin.CharacterProfiles.GetCurrentProfilePath();
        if (string.Equals(currentPath, loadedPublicProfilePath, StringComparison.OrdinalIgnoreCase)) return;
        LoadPublicProfileDraft();
    }

    private void LoadPublicProfileDraft()
    {
        loadedPublicProfilePath = plugin.CharacterProfiles.GetCurrentProfilePath();
        if (plugin.CharacterProfiles.TryReadCurrent(out var profile, out var error))
        {
            draftPublicCharacterPrompt = profile;
            publicProfileLoadError = CharacterProfileFileError.None;
            return;
        }

        draftPublicCharacterPrompt = "";
        publicProfileLoadError = error;
    }

    private string GetProfileFileErrorMessage(CharacterProfileFileError error)
    {
        return error switch
        {
            CharacterProfileFileError.NotLoggedIn => T(
                "Log in before selecting or saving a player character profile.",
                "プレイヤー本人の人物像を選択・保存する前にログインしてください。"),
            CharacterProfileFileError.NotFound => T(
                "The selected profile file was not found.",
                "選択した人物像ファイルが見つかりません。"),
            CharacterProfileFileError.TooLong => T(
                $"The character profile exceeds {Configuration.MaxCharacterPromptLength} characters.",
                $"人物像ファイルが{Configuration.MaxCharacterPromptLength}文字を超えています。"),
            CharacterProfileFileError.InvalidEncoding => T(
                "The character profile must be a valid UTF-8 text file.",
                "人物像ファイルは正しいUTF-8のテキストにしてください。"),
            CharacterProfileFileError.WriteFailed => T(
                "Failed to save the character profile file.",
                "人物像ファイルを保存できませんでした。"),
            _ => T(
                "Failed to read the character profile file.",
                "人物像ファイルを読み込めませんでした。"),
        };
    }

    private string T(string english, string japanese) => UiText.T(draftLanguage, english, japanese);

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static float GetContentItemWidth(float desiredWidth)
    {
        return MathF.Min(desiredWidth, ImGui.GetContentRegionAvail().X);
    }

    private static string GetPostChannelLabel(GameChatPostChannel channel)
    {
        return channel switch
        {
            GameChatPostChannel.Say => "Say",
            GameChatPostChannel.Party => "Party",
            GameChatPostChannel.FreeCompany => "Free Company",
            GameChatPostChannel.Tell => "Tell",
            _ => "Say",
        };
    }
}
