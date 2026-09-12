using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AIRoleplay;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public const int MaxCharacterPromptLength = 500;
    public const int MaxAssistantDisplayNameLength = 50;
    public const int DefaultGameChatLineLimit = 20;
    public const int MinGameChatLineLimit = 1;
    public const int MaxGameChatLineLimit = 100;
    public const int DefaultAutoResponseChatLineThreshold = 50;
    public const int MinAutoResponseChatLineThreshold = 1;
    public const int MaxAutoResponseChatLineThreshold = 100;
    public const int MaxModelSlots = 3;

    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public bool SomePropertyToBeSavedAndWithADefault { get; set; } = true;
    public string CharacterPrompt { get; set; } = "";
    public string AssistantDisplayName { get; set; } = "";
    public string PublicAssistantDisplayName { get; set; } = "";
    public bool ShowAiResponseInLocalChat { get; set; }
    public bool ReplyToOwnSayMessages { get; set; }
    public bool EnablePersonalAutoResponseByChatLines { get; set; }
    public bool EnablePrivateConversationSummary { get; set; }
    public int PersonalAutoResponseChatLineThreshold { get; set; } = DefaultAutoResponseChatLineThreshold;
    public bool EnableAutoResponseByChatLines { get; set; }
    public bool EnablePublicConversationSummary { get; set; }
    public PublicAutoResponseTrigger PublicAutoResponseTrigger { get; set; } = PublicAutoResponseTrigger.CapturedMessageCount;
    public bool PostReplyAssistToGameChat { get; set; }
    public GameChatPostChannel ReplyAssistPostChannel { get; set; } = GameChatPostChannel.Say;
    public int AutoResponseChatLineThreshold { get; set; } = DefaultAutoResponseChatLineThreshold;
    public bool EnableFileLogging { get; set; }
    public string LogFilePath { get; set; } = "";
    public bool EnablePrivateLogging { get; set; }
    public bool EnablePublicLogging { get; set; }
    public string PrivateLogDirectory { get; set; } = "";
    public string PublicLogDirectory { get; set; } = "";
    public int GameChatLineLimit { get; set; } = DefaultGameChatLineLimit;
    public bool CaptureSay { get; set; } = true;
    public bool CaptureShoutYell { get; set; }
    public bool CaptureTell { get; set; }
    public bool CaptureParty { get; set; }
    public bool CaptureFreeCompany { get; set; }
    public bool CaptureLinkshell { get; set; }
    public bool CaptureNoviceNetwork { get; set; }
    public bool CapturePvpTeam { get; set; }
    public bool CaptureNpcDialogue { get; set; }
    public bool CaptureEmote { get; set; }
    public bool CaptureRandomNumber { get; set; }
    public bool CaptureAlarm { get; set; }
    public bool CaptureOrchestrion { get; set; }
    public UiLanguage Language { get; set; } = UiLanguage.English;
    public List<LlmModelSlot> ModelSlots { get; set; } = CreateDefaultModelSlots();
    public List<CachedLlmModel> CachedModels { get; set; } = CreateDefaultModelCache();

    public string GetLimitedCharacterPrompt()
    {
        return CharacterPrompt.Length <= MaxCharacterPromptLength
            ? CharacterPrompt
            : CharacterPrompt[..MaxCharacterPromptLength];
    }

    public string GetLimitedAssistantDisplayName()
    {
        return AssistantDisplayName.Length <= MaxAssistantDisplayNameLength
            ? AssistantDisplayName
            : AssistantDisplayName[..MaxAssistantDisplayNameLength];
    }

    public string GetLimitedPublicAssistantDisplayName()
    {
        return PublicAssistantDisplayName.Length <= MaxAssistantDisplayNameLength
            ? PublicAssistantDisplayName
            : PublicAssistantDisplayName[..MaxAssistantDisplayNameLength];
    }

    public int GetClampedGameChatLineLimit()
    {
        return Math.Clamp(GameChatLineLimit, MinGameChatLineLimit, MaxGameChatLineLimit);
    }

    public int GetClampedAutoResponseChatLineThreshold()
    {
        return Math.Clamp(
            AutoResponseChatLineThreshold,
            MinAutoResponseChatLineThreshold,
            MaxAutoResponseChatLineThreshold);
    }

    public int GetClampedPersonalAutoResponseChatLineThreshold()
    {
        return Math.Clamp(
            PersonalAutoResponseChatLineThreshold,
            MinAutoResponseChatLineThreshold,
            MaxAutoResponseChatLineThreshold);
    }

    public IReadOnlyList<LlmModelSlot> GetModelSlots()
    {
        EnsureModelConfiguration();
        return ModelSlots.Select(slot => slot.Clone()).ToList();
    }

    public IReadOnlyList<LlmModelSlot> GetEnabledModelSlots()
    {
        EnsureModelConfiguration();
        return ModelSlots
            .Where(slot => slot.Enabled && !string.IsNullOrWhiteSpace(slot.ModelId))
            .Select(slot => slot.Clone())
            .ToList();
    }

    public void SetModelSlots(IEnumerable<LlmModelSlot> slots)
    {
        ModelSlots = slots
            .Take(MaxModelSlots)
            .Select(slot => slot.Clone())
            .ToList();
        EnsureModelConfiguration();
    }

    public IReadOnlyList<string> GetCachedModelIds(LlmProvider provider)
    {
        EnsureModelConfiguration();
        return CachedModels
            .Where(model => model.Provider == provider && !string.IsNullOrWhiteSpace(model.ModelId))
            .Select(model => model.ModelId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetCachedModels(LlmProvider provider, IEnumerable<string> modelIds)
    {
        EnsureModelConfiguration();
        CachedModels.RemoveAll(model => model.Provider == provider);
        CachedModels.AddRange(modelIds
            .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .Select(modelId => new CachedLlmModel
            {
                Provider = provider,
                ModelId = modelId,
            }));
    }

    public static IReadOnlyList<string> GetDefaultModelIds(LlmProvider provider) =>
        provider switch
        {
            LlmProvider.OpenAI =>
            [
                "gpt-5.1",
                "gpt-5.1-codex",
                "gpt-5.1-codex-max",
                "gpt-5-pro",
                "gpt-5",
                "gpt-5-mini",
                "gpt-5-nano",
                "gpt-5-codex",
                "gpt-4.1",
                "gpt-4.1-mini",
                "gpt-4.1-nano",
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-4-turbo",
                "gpt-3.5-turbo",
                "o3",
                "o3-pro",
                "o4-mini",
            ],
            LlmProvider.Claude =>
            [
                "claude-fable-5",
                "claude-opus-5",
                "claude-opus-4-8",
                "claude-opus-4-7",
                "claude-opus-4-6",
                "claude-opus-4-5-20251101",
                "claude-sonnet-5",
                "claude-sonnet-4-6",
                "claude-sonnet-4-5-20250929",
                "claude-haiku-4-5-20251001",
            ],
            LlmProvider.Gemini =>
            [
                "gemini-3.8-flash",
                "gemini-3.7-flash",
                "gemini-3.6-flash",
                "gemini-3.5-flash",
                "gemini-3.5-flash-lite",
                "gemini-3.1-pro-preview",
                "gemini-3.1-flash-lite",
                "gemini-3-flash-preview",
            ],
            LlmProvider.DeepSeek =>
            [
                "deepseek-v4-flash",
                "deepseek-v4-pro",
                "deepseek-v4-flash-vision-exp",
            ],
            LlmProvider.Zai =>
            [
                "glm-5.1",
                "glm-5-turbo",
                "glm-5",
                "glm-4.7",
                "glm-4.7-flash",
                "glm-4.7-flashx",
                "glm-4.6",
                "glm-4.5",
                "glm-4.5-air",
                "glm-4.5-x",
                "glm-4.5-airx",
                "glm-4.5-flash",
                "glm-4-32b-0414-128k",
            ],
            _ => [],
        };

    public void EnsureModelConfiguration()
    {
        ModelSlots ??= new List<LlmModelSlot>();
        CachedModels ??= new List<CachedLlmModel>();

        if (ModelSlots.Count == 0)
        {
            ModelSlots.AddRange(CreateDefaultModelSlots());
        }

        while (ModelSlots.Count < MaxModelSlots)
        {
            ModelSlots.Add(new LlmModelSlot());
        }

        if (ModelSlots.Count > MaxModelSlots)
        {
            ModelSlots = ModelSlots.Take(MaxModelSlots).ToList();
        }

        if (CachedModels.Count == 0)
        {
            CachedModels.AddRange(CreateDefaultModelCache());
        }

        foreach (var provider in Enum.GetValues<LlmProvider>())
        {
            if (CachedModels.Any(model => model.Provider == provider))
            {
                continue;
            }

            CachedModels.AddRange(GetDefaultModelIds(provider)
                .Select(modelId => new CachedLlmModel
                {
                    Provider = provider,
                    ModelId = modelId,
                }));
        }
    }

    public bool Migrate()
    {
        var changed = false;

        CharacterPrompt ??= "";
        AssistantDisplayName ??= "";
        PublicAssistantDisplayName ??= "";
        LogFilePath ??= "";
        ModelSlots ??= new List<LlmModelSlot>();
        CachedModels ??= new List<CachedLlmModel>();

        if (Version < 1)
        {
            PublicAssistantDisplayName = AssistantDisplayName;
            Version = 1;
            changed = true;
        }

        if (Version < 2)
        {
            EnablePrivateLogging = EnableFileLogging;
            EnablePublicLogging = EnableFileLogging;
            try
            {
                var directory = System.IO.Path.GetDirectoryName(LogFilePath) ?? "";
                PrivateLogDirectory = directory;
                PublicLogDirectory = directory;
            }
            catch (ArgumentException) { }
            PostReplyAssistToGameChat = false;
            Version = 2;
            changed = true;
        }

        PrivateLogDirectory ??= "";
        PublicLogDirectory ??= "";
        if (!Enum.IsDefined(PublicAutoResponseTrigger))
        {
            PublicAutoResponseTrigger = PublicAutoResponseTrigger.CapturedMessageCount;
            changed = true;
        }
        EnsureModelConfiguration();
        return changed;
    }

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        CharacterPrompt ??= "";
        AssistantDisplayName ??= "";
        PublicAssistantDisplayName ??= "";
        LogFilePath ??= "";

        if (CharacterPrompt.Length > MaxCharacterPromptLength)
        {
            CharacterPrompt = CharacterPrompt[..MaxCharacterPromptLength];
        }

        if (AssistantDisplayName.Length > MaxAssistantDisplayNameLength)
        {
            AssistantDisplayName = AssistantDisplayName[..MaxAssistantDisplayNameLength];
        }

        if (PublicAssistantDisplayName.Length > MaxAssistantDisplayNameLength)
        {
            PublicAssistantDisplayName = PublicAssistantDisplayName[..MaxAssistantDisplayNameLength];
        }

        GameChatLineLimit = GetClampedGameChatLineLimit();
        PersonalAutoResponseChatLineThreshold = GetClampedPersonalAutoResponseChatLineThreshold();
        AutoResponseChatLineThreshold = GetClampedAutoResponseChatLineThreshold();
        if (!Enum.IsDefined(PublicAutoResponseTrigger))
            PublicAutoResponseTrigger = PublicAutoResponseTrigger.CapturedMessageCount;
        EnsureModelConfiguration();

        Plugin.PluginInterface.SavePluginConfig(this);
    }

    private static List<LlmModelSlot> CreateDefaultModelSlots() =>
        new()
        {
            new LlmModelSlot
            {
                Enabled = true,
                Provider = LlmProvider.Zai,
                ModelId = "glm-4.5-flash",
            },
            new LlmModelSlot(),
            new LlmModelSlot(),
        };

    private static List<CachedLlmModel> CreateDefaultModelCache() =>
        Enum.GetValues<LlmProvider>()
            .SelectMany(provider => GetDefaultModelIds(provider)
                .Select(modelId => new CachedLlmModel
                {
                    Provider = provider,
                    ModelId = modelId,
                }))
            .ToList();
}

public enum PublicAutoResponseTrigger
{
    CapturedMessageCount,
    RecipientSpeech,
}

public enum GameChatPostChannel
{
    Say,
    Party,
    FreeCompany,
    Tell,
}

[Serializable]
public class LlmModelSlot
{
    public bool Enabled { get; set; }
    public LlmProvider Provider { get; set; } = LlmProvider.Zai;
    public string ModelId { get; set; } = "";

    public LlmModelSlot Clone() =>
        new()
        {
            Enabled = Enabled,
            Provider = Provider,
            ModelId = ModelId,
        };
}

[Serializable]
public class CachedLlmModel
{
    public LlmProvider Provider { get; set; }
    public string ModelId { get; set; } = "";
}
