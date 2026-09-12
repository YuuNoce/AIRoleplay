using System;
using System.Linq;
using System.Text;

namespace AIRoleplay;

public enum TalkMode { Private, PublicAssist, PublicDirect }
public enum ReplyTargetMode { Conversation, Named, CurrentTarget, Party }

// Shared by the native input bridge and tests; never truncate or split a public utterance.
public static class ChatSafety
{
    public const int MaxChatBytes = 500;
    public static bool IsPartyChannel(string channel) => channel is "Party" or "CrossParty";

    public static bool TryNormalizeTellRecipient(string? value, out string playerName, out string address)
    {
        playerName = "";
        address = "";
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOf('@');
        if (separator <= 0 || separator != trimmed.IndexOf('@') || separator == trimmed.Length - 1) return false;

        var nameParts = trimmed[..separator]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var world = trimmed[(separator + 1)..].Trim();
        if (nameParts.Length != 2 || !nameParts.All(IsValidNamePart) ||
            world.Length == 0 || !world.All(character => char.IsLetterOrDigit(character) || character == '-'))
            return false;

        playerName = string.Join(' ', nameParts);
        address = $"{playerName}@{world}";
        return true;
    }

    public static string BuildCommand(string text, GameChatPostChannel channel, string? tellRecipient = null)
    {
        var body = string.Join(" ", text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("The draft is empty. / 発言が空です。");
        if (body.Any(char.IsControl) || body.Contains('<') || body.Contains('>'))
            throw new InvalidOperationException("Control characters and chat placeholders are not allowed. / 制御文字・チャット置換表現は使用できません。");
        var prefix = channel switch
        {
            GameChatPostChannel.Say => "/s",
            GameChatPostChannel.Party => "/p",
            GameChatPostChannel.FreeCompany => "/fc",
            GameChatPostChannel.Tell when TryNormalizeTellRecipient(tellRecipient, out _, out var address) => $"/tell {address}",
            GameChatPostChannel.Tell => throw new InvalidOperationException(
                "Tell requires Firstname Lastname@World. / Tellの相手はFirstname Lastname@World形式で指定してください。"),
            _ => throw new InvalidOperationException("Invalid channel. / 投稿先が不正です。"),
        };
        var command = prefix + " " + body;
        if (Encoding.UTF8.GetByteCount(command) > MaxChatBytes)
            throw new InvalidOperationException("Draft exceeds 500 UTF-8 bytes. Shorten it before posting. / 発言が500バイトを超えています。短く編集してください。");
        return command;
    }

    private static bool IsValidNamePart(string value) =>
        value.Length > 0 && value.All(character => char.IsLetter(character) || character is '\'' or '-');
}
