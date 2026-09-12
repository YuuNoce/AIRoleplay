using System;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;

namespace AIRoleplay.Windows;

public sealed class LogSettingsPanel
{
    private readonly Plugin plugin;
    private readonly FileDialogManager dialogs = new();
    public LogSettingsPanel(Plugin plugin) => this.plugin = plugin;
    public void DrawDialogs() => dialogs.Draw();

    public void Draw(bool isPublic)
    {
        var config = plugin.Configuration;
        string T(string en, string ja) => UiText.T(config.Language, en, ja);
        ImGui.PushID(isPublic ? "PublicLog" : "PrivateLog");
        ImGui.Text(T("Conversation log files", "会話ログファイル"));
        var enabled = isPublic ? config.EnablePublicLogging : config.EnablePrivateLogging;
        if (ImGui.Checkbox(T("Save conversation log", "会話ログを保存する"), ref enabled))
        {
            if (isPublic) config.EnablePublicLogging = enabled;
            else config.EnablePrivateLogging = enabled;
            config.Save();
        }
        ImGui.TextDisabled(T(
            "Full prompts and API diagnostics are not included.",
            "完全なプロンプトとAPI診断情報は含まれません。"));
        ImGui.TextWrapped(plugin.AIRoleplayLogger.GetLogPath(isPublic));
        if (ImGui.Button(T("Choose folder...", "保存先フォルダ...")))
        {
            var directory = plugin.AIRoleplayLogger.GetDirectory(isPublic);
            dialogs.OpenFolderDialog(T("Log folder", "ログ保存先"), (ok, path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                if (isPublic) config.PublicLogDirectory = path;
                else config.PrivateLogDirectory = path;
                config.Save();
            }, Directory.Exists(directory) ? directory : Plugin.PluginInterface.ConfigDirectory.FullName);
        }
        ImGui.SameLine();
        if (ImGui.Button(T("Use default", "既定の保存先")))
        {
            if (isPublic) config.PublicLogDirectory = "";
            else config.PrivateLogDirectory = "";
            config.Save();
        }
        ImGui.PopID();
    }
}
