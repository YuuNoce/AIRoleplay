using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AIRoleplay;

public sealed class AIRoleplayLogger
{
    // Developer-only diagnostics. Enable temporarily in code when investigating API or prompt issues.
    private static readonly bool EnableApiDiagnosticLog = false;
    private static readonly bool EnablePromptDiagnosticLog = false;
    private readonly object syncRoot = new();
    private readonly Configuration configuration;
    public AIRoleplayLogger(Configuration configuration) => this.configuration = configuration;
    public static string DefaultLogDirectory => Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "logs");

    public string GetDirectory(bool isPublic)
    {
        var value = isPublic ? configuration.PublicLogDirectory : configuration.PrivateLogDirectory;
        return string.IsNullOrWhiteSpace(value) ? DefaultLogDirectory : value;
    }

    public string GetLogPath(bool isPublic)
    {
        var character = GetCurrentCharacterFolder();
        return Path.Combine(GetDirectory(isPublic), character ?? "(not logged in)", isPublic ? "public.log" : "private.log");
    }

    public LogDestination? Capture(bool isPublic)
    {
        var character = GetCurrentCharacterFolder();
        if (character == null) return null;
        return new LogDestination(character, GetDirectory(isPublic), isPublic);
    }

    public static string? GetCurrentCharacterFolder()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (!Plugin.ClientState.IsLoggedIn || player == null) return null;
        var name = player.Name.TextValue;
        var world = player.HomeWorld.Value.Name.ToString();
        return SanitizeFolder(name + "@" + world);
    }

    public static string SanitizeFolder(string value) =>
        string.Concat(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).TrimEnd(' ', '.');

    public void Log(LogDestination? destination, string stage, string text)
    {
        if (!EnableApiDiagnosticLog || destination == null) return;
        Write(destination, destination.IsPublic ? "public.log" : "private.log",
            stage + " " + SingleLine(text));
    }

    public void LogApiResponse(LogDestination? destination, LlmResponseDiagnostics diagnostics)
    {
        if (!EnablePromptDiagnosticLog || destination == null) return;
        // Both modes use one diagnostics file in the private log root.
        Write(destination with { Directory = GetDirectory(false) }, "api.log",
            (destination.IsPublic ? "PUBLIC " : "PRIVATE ") + diagnostics.ToLogLine());
    }

    public void LogPrompt(LogDestination? destination, IReadOnlyList<LlmChatMessage> messages)
    {
        if (destination == null || !IsEnabled(destination)) return;
        var prompt = new StringBuilder();
        prompt.AppendLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {(destination.IsPublic ? "PUBLIC" : "PRIVATE")} PROMPT");
        foreach (var message in messages)
        {
            prompt.AppendLine($"--- {message.Role} ---");
            prompt.AppendLine(message.Content);
        }
        prompt.AppendLine("--- END PROMPT ---");

        WriteMultiline(destination, "prompt.log", prompt.ToString());
    }

    private bool IsEnabled(LogDestination destination) =>
        destination.IsPublic ? configuration.EnablePublicLogging : configuration.EnablePrivateLogging;

    private void Write(LogDestination destination, string file, string message)
    {
        try
        {
            lock (syncRoot)
            {
                var directory = Path.Combine(destination.Directory, destination.Character);
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, file),
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {SingleLine(message)}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Failed to write AIRoleplay log."); }
    }

    private void WriteMultiline(LogDestination destination, string file, string message)
    {
        try
        {
            lock (syncRoot)
            {
                var directory = Path.Combine(destination.Directory, destination.Character);
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, file), message + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Failed to write AIRoleplay log."); }
    }

    private static string SingleLine(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n");
}

public sealed record LogDestination(string Character, string Directory, bool IsPublic);
