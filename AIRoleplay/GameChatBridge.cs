using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AIRoleplay;

// Call only on the framework/UI thread. Never retain game pointers across frames.
public sealed unsafe class GameChatBridge
{
    private string? ownedText;
    private string? ownedRawText;
    public bool IsLinked => ownedText != null;

    private static AtkComponentTextInput* GetInput()
    {
        var addon = (AddonChatLog*)Plugin.GameGui.GetAddonByName("ChatLog").Address;
        return addon == null ? null : addon->TextInput;
    }

    public bool Poll()
    {
        if (ownedText == null) return false;
        var input = GetInput();
        if (input != null && input->EvaluatedString.ToString() == ownedText &&
            input->RawString.ToString() == ownedRawText) return false;
        ownedText = null;
        ownedRawText = null;
        return true;
    }

    public void Stop(bool clearOwnedText)
    {
        if (clearOwnedText && ownedText != null)
        {
            var input = GetInput();
            if (input != null && input->EvaluatedString.ToString() == ownedText &&
                input->RawString.ToString() == ownedRawText)
                input->SetText("");
        }
        ownedText = null;
        ownedRawText = null;
    }

    public void PutDraft(string command)
    {
        var input = GetInput();
        if (input == null)
            throw new InvalidOperationException("Chat input is unavailable. / チャット入力欄を取得できません。");
        var current = input->EvaluatedString.ToString();
        var raw = input->RawString.ToString();
        if (ownedText == null ? !string.IsNullOrEmpty(current) || !string.IsNullOrEmpty(raw)
            : current != ownedText || raw != ownedRawText)
        {
            ownedText = null;
            ownedRawText = null;
            throw new InvalidOperationException("Chat input was edited or already contains text. / チャット欄に入力済みの文章があります。上書きしません。");
        }
        input->SetText(command);
        if (input->EvaluatedString.ToString() != command)
        {
            input->SetText(raw);
            ownedText = null;
            throw new InvalidOperationException("The game changed the draft; input was restored. / ゲーム側で文章が変更されたため入力を元に戻しました。");
        }
        ownedText = command;
        ownedRawText = input->RawString.ToString();
    }

    public void Send(string command, GameChatPostChannel channel)
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null)
            throw new InvalidOperationException("Not logged in. / ログインしていません。");
        if (channel == GameChatPostChannel.Party && Plugin.PartyList.Length == 0)
            throw new InvalidOperationException("Not in a party. / パーティに参加していません。");
        var ui = UIModule.Instance();
        if (ui == null) throw new InvalidOperationException("Game UI is unavailable.");
        var text = Utf8String.FromString(command);
        try { ui->ProcessChatBoxEntry(text); }
        finally { text->Dtor(true); }
    }
}
