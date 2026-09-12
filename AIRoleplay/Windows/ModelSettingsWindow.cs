using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AIRoleplay.Windows;

public sealed class ModelSettingsWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly LlmFallbackClient llmClient;
    private readonly WindowsCredentialApiKeyReader apiKeyReader;
    private readonly Dictionary<LlmProvider, bool> keyStatus = new();
    private readonly List<LlmModelSlot> draftSlots = new();
    private readonly bool[] isTesting = new bool[Configuration.MaxModelSlots];
    private readonly string[] testStatuses = new string[Configuration.MaxModelSlots];
    private string statusText = "";
    private bool isRefreshing;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentQueue<Action> updates = new();
    private readonly object stateLock = new();
    private volatile bool disposed;

    public ModelSettingsWindow(Plugin plugin)
        : base("AIRoleplay LLM Models###AIRoleplayModelSettings")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        configuration = plugin.Configuration;
        llmClient = plugin.LlmClient;
        apiKeyReader = plugin.ApiKeyReader;
        ReloadCredentialStatus();
        LoadDraftFromConfiguration();
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
    }

    public override void Update()
    {
        lock (stateLock)
        {
            while (updates.TryDequeue(out var update))
                if (!disposed) update();
        }
    }

    public override void Draw()
    {
        DrawContent(closeOnCancel: true);
    }

    public void DrawEmbedded()
    {
        DrawContent(closeOnCancel: false);
    }

    private void DrawContent(bool closeOnCancel)
    {
        lock (stateLock)
        {
            if (!disposed) DrawContentCore(closeOnCancel);
        }
    }

    private void DrawContentCore(bool closeOnCancel)
    {
        ImGui.Text(T("LLM Model Settings", "LLMモデル設定"));
        ImGui.Spacing();

        DrawCredentialStatus();

        ImGui.Spacing();
        if (ImGui.Button(isRefreshing ? T("Updating...", "更新中...") : T("Update LLM model list", "LLMモデル一覧を更新")) && !isRefreshing)
        {
            isRefreshing = true;
            statusText = T("Updating LLM model list...", "LLMモデル一覧を更新中...");
            _ = RefreshModelsAsync();
        }

        ImGui.SameLine();
        if (ImGui.Button(T("Reload key status", "キー状態を再読み込み")))
        {
            ReloadCredentialStatus();
            statusText = T("Reloaded key status.", "キー状態を再読み込みしました。");
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1.0f, 0.78f, 0.25f, 1.0f), T(
            "* Each test sends a short request and uses a small amount of model tokens.",
            "※ 各テストは短いリクエストが送信されるため、モデルのトークンを少量消費します。"));
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        for (var index = 0; index < Configuration.MaxModelSlots; index++)
        {
            if (index > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }

            DrawSlot(index);
            ImGui.Spacing();
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button(T("Save", "保存")))
        {
            SaveDraft();
            statusText = T("Saved.", "保存しました。");
        }

        ImGui.SameLine();
        if (ImGui.Button(T("Cancel", "キャンセル")))
        {
            LoadDraftFromConfiguration();
            statusText = T("Canceled.", "キャンセルしました。");
            if (closeOnCancel)
            {
                IsOpen = false;
            }
        }

        if (!string.IsNullOrWhiteSpace(statusText))
        {
            ImGui.Spacing();
            ImGui.TextWrapped(statusText);
        }
    }

    private void DrawCredentialStatus()
    {
        foreach (var provider in Enum.GetValues<LlmProvider>())
        {
            var label = keyStatus.TryGetValue(provider, out var hasApiKey) && hasApiKey
                ? T("configured", "設定済み")
                : T("not configured", "未設定");
            ImGui.Text($"{provider}: {label}");
        }
    }

    public void DrawSetupInstructions()
    {
        ImGui.Text(T("LLM API Key Setup", "LLM APIキー登録方法"));
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextWrapped(T(
            "Register your own LLM API keys in Windows Credential Manager. AIRoleplay and its distributor do not provide API keys.",
            "ご自身で取得したLLMのAPIキーを、Windows資格情報マネージャーに登録してください。AIRoleplayおよび配布者はAPIキーを提供しません。"));
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.Text(T("Registration Steps", "登録手順"));
        DrawSetupStepTable();
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.Text(T("Credential Target by Service", "サービス別 登録名"));
        DrawCredentialTargetTable();
        ImGui.TextWrapped(T(
            "Register only the services you want to use. You do not need to register every service.",
            "使用したいサービスの分だけ登録してください（全登録は不要）。"));
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.Text(T("About LLM Priority", "LLM優先順位について"));
        ImGui.TextWrapped(T(
            "Every request always starts with #1. #2 and #3 are tried in order only when the previous model fails or returns an empty response. The next request starts with #1 again.",
            "各リクエストは常に#1から開始します。#1が失敗または空応答のときだけ、#2、#3を順に試します。次回のリクエストでは再び#1から開始します。"));
        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.TextWrapped(T(
            "* Each test sends a short request and uses a small amount of model tokens.",
            "※ 各テストは短いリクエストが送信されるため、モデルのトークンを少量消費します。"));
    }

    private void DrawSetupStepTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("AIRoleplayLlmSetupSteps", 2, flags, new Vector2(0, 0)))
        {
            return;
        }

        ImGui.TableSetupColumn(T("Step", "手順"), ImGuiTableColumnFlags.WidthFixed, 55.0f);
        ImGui.TableSetupColumn(T("Action", "操作内容"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        DrawTableRow("1", T(
            "Search for Credential Manager on your Windows machine and open it.",
            "Windowsマシンで「資格情報マネージャー」を検索して開く"));
        DrawTableRow("2", T(
            "Select Windows Credentials -> Add a generic credential.",
            "「Windows資格情報」→「汎用資格情報の追加」を選択"));
        DrawTableRow("3", T(
            "Enter the target name from the table below in Internet or network address.",
            "「インターネットまたはネットワークのアドレス」に下表の登録名を入力"));
        DrawTableRow("4", T(
            "Enter AIRoleplay as User name.",
            "「ユーザー名」に AIRoleplay と入力"));
        DrawTableRow("5", T(
            "Enter your actual API key as Password and save it.",
            "「パスワード」に実際のAPIキーを入力して保存"));
        DrawTableRow("6", T(
            "Open LLM Models and click Reload key status.",
            "LLMモデル設定画面で「キー状態を再読み込み」を押す"));

        ImGui.EndTable();
    }

    private void DrawCredentialTargetTable()
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("AIRoleplayLlmCredentialTargets", 2, flags, new Vector2(0, 0)))
        {
            return;
        }

        ImGui.TableSetupColumn(T("Service", "サービス"), ImGuiTableColumnFlags.WidthFixed, 110.0f);
        ImGui.TableSetupColumn(T("Internet or network address", "インターネットまたはネットワークのアドレス"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var provider in Enum.GetValues<LlmProvider>())
        {
            DrawTableRow(provider.ToString(), apiKeyReader.GetCredentialTarget(provider));
        }

        ImGui.EndTable();
    }

    private static void DrawTableRow(string firstColumn, string secondColumn)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(firstColumn);
        ImGui.TableNextColumn();
        ImGui.TextWrapped(secondColumn);
    }

    private void DrawSlot(int index)
    {
        var slot = draftSlots[index];
        var priorityNumber = index + 1;
        ImGui.PushID(index);
        ImGui.Text(T($"LLM Priority #{priorityNumber}", $"LLM優先順位 #{priorityNumber}"));

        var enabled = slot.Enabled;
        if (ImGui.Checkbox(T($"Enable #{priorityNumber}", $"#{priorityNumber}を有効にする"), ref enabled))
        {
            slot.Enabled = enabled;
        }

        ImGui.SetNextItemWidth(170);
        if (ImGui.BeginCombo(T("Provider", "Provider"), slot.Provider.ToString()))
        {
            foreach (var provider in Enum.GetValues<LlmProvider>())
            {
                if (ImGui.Selectable(provider.ToString(), slot.Provider == provider))
                {
                    slot.Provider = provider;
                    var models = configuration.GetCachedModelIds(provider);
                    slot.ModelId = models.FirstOrDefault() ?? "";
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(-1);
        var modelId = slot.ModelId;
        if (ImGui.InputText(T("Model ID", "Model ID"), ref modelId, 160))
        {
            slot.ModelId = modelId.Trim();
        }

        var modelIds = configuration.GetCachedModelIds(slot.Provider);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(T("Choose from LLM model list", "LLMモデル一覧から選択"), T("(LLM model list)", "(LLMモデル一覧)")))
        {
            foreach (var cachedModelId in modelIds)
            {
                if (ImGui.Selectable(cachedModelId, string.Equals(slot.ModelId, cachedModelId, StringComparison.OrdinalIgnoreCase)))
                {
                    slot.ModelId = cachedModelId;
                }
            }

            if (modelIds.Count == 0)
            {
                ImGui.TextDisabled(T("Update LLM model list first.", "先にLLMモデル一覧を更新してください。"));
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        if (ImGui.Button(isTesting[index]
                ? T($"Testing #{priorityNumber}...", $"#{priorityNumber}をテスト中...")
                : T(
                    $"Test #{priorityNumber} (uses a small amount of LLM tokens)",
                    $"#{priorityNumber}をテストする（LLMトークンを少量消費します）")) && !isTesting[index])
        {
            isTesting[index] = true;
            testStatuses[index] = T("Testing...", "テスト中...");
            var provider = slot.Provider;
            var testModelId = slot.ModelId.Trim();
            _ = TestModelAsync(index, provider, testModelId);
        }

        if (!string.IsNullOrWhiteSpace(testStatuses[index]))
        {
            ImGui.SameLine();
            ImGui.TextWrapped(testStatuses[index]);
        }

        ImGui.PopID();
    }

    private async Task RefreshModelsAsync()
    {
        var cancellationToken = lifetime.Token;
        ReloadCredentialStatus();
        var providers = Enum.GetValues<LlmProvider>().Where(p => keyStatus[p]).ToList();
        var results = new Dictionary<LlmProvider, IReadOnlyList<LlmModelInfo>>();
        var failed = providers.Count != Enum.GetValues<LlmProvider>().Length;
        foreach (var provider in providers)
        {
            try
            {
                var models = await llmClient.ListModelsAsync(provider, cancellationToken);
                if (models.Count > 0) results[provider] = models;
                else failed = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                failed = true;
                Plugin.Log.Warning(ex, "Failed to refresh model list. Provider={Provider}", provider);
            }
        }
        updates.Enqueue(() =>
        {
            try
            {
                foreach (var (provider, models) in results)
                    configuration.SetCachedModels(provider, models.Select(model => model.Id));
                configuration.Save();
                statusText = failed
                    ? T("Some lists could not be fetched; previous lists were kept.", "取得できないProviderは以前の一覧を維持しました。")
                    : T("Updated model lists.", "モデル一覧を更新しました。");
            }
            catch (Exception ex) { statusText = ex.Message; }
            finally { isRefreshing = false; }
        });
    }

    private void LoadDraftFromConfiguration()
    {
        draftSlots.Clear();
        draftSlots.AddRange(configuration.GetModelSlots().Select(slot => slot.Clone()));
        while (draftSlots.Count < Configuration.MaxModelSlots)
        {
            draftSlots.Add(new LlmModelSlot());
        }
    }

    private void SaveDraft()
    {
        configuration.SetModelSlots(draftSlots);
        configuration.Save();
    }

    private async Task TestModelAsync(int index, LlmProvider provider, string modelId)
    {
        var cancellationToken = lifetime.Token;
        ReloadCredentialStatus();
        try
        {
            var result = await llmClient.TestModelAsync(provider, modelId, cancellationToken);
            updates.Enqueue(() => testStatuses[index] = $"{provider}/{modelId}: OK ({result.Diagnostics.DurationMs} ms)");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            updates.Enqueue(() => testStatuses[index] = T($"Failed: {ex.Message}", $"失敗: {ex.Message}"));
            Plugin.Log.Warning(ex, "LLM model test failed. Provider={Provider} Model={Model}", provider, modelId);
        }
        finally
        {
            updates.Enqueue(() => isTesting[index] = false);
        }
    }

    private void ReloadCredentialStatus()
    {
        keyStatus.Clear();
        foreach (var provider in Enum.GetValues<LlmProvider>())
        {
            keyStatus[provider] = apiKeyReader.HasApiKey(provider);
        }
    }

    private string T(string english, string japanese) => UiText.T(configuration.Language, english, japanese);
}
