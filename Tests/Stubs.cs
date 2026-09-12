namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace AIRoleplay
{
    internal static class Plugin
    {
        public static ConfigStore PluginInterface { get; } = new();
        public static TestLog Log { get; } = new();
    }
    internal sealed class ConfigStore
    {
        public void SavePluginConfig(Configuration configuration) { }
    }
    internal sealed class TestLog
    {
        public void Warning(Exception ex, string message, params object[] args) { }
    }
    public sealed class WindowsCredentialApiKeyReader
    {
        public bool HasApiKey(LlmProvider provider) => true;
        public bool TryReadApiKey(LlmProvider provider, out string key, out string? error)
        {
            key = "test-only-no-network";
            error = null;
            return true;
        }
    }
}
