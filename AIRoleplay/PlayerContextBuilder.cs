using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;

namespace AIRoleplay;

public sealed class PlayerContextBuilder
{
    public string BuildLocalPlayerContext(UiLanguage language)
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return "";
        }

        var lines = new List<string>();
        var name = localPlayer.Name.TextValue;
        if (!string.IsNullOrWhiteSpace(name))
        {
            lines.Add($"{UiText.T(language, "Name", "名前")}: {name}");
        }

        if (localPlayer is ICharacter character)
        {
            var customize = character.CustomizeData;
            var raceName = GetRaceName(customize.Race, customize.Sex);
            if (!string.IsNullOrWhiteSpace(raceName))
            {
                lines.Add($"{UiText.T(language, "Race", "種族")}: {raceName}");
            }

            var tribeName = GetTribeName(customize.Tribe, customize.Sex);
            if (!string.IsNullOrWhiteSpace(tribeName))
            {
                lines.Add($"{UiText.T(language, "Clan", "部族")}: {tribeName}");
            }

            lines.Add($"{UiText.T(language, "Sex", "性別")}: {GetSexName(customize.Sex, language)}");
        }

        return lines.Count == 0
            ? ""
            : UiText.T(language, "[Player character information]\n", "【ユーザーキャラクター情報】\n") + string.Join("\n", lines);
    }

    private static string GetRaceName(byte raceId, byte sex)
    {
        return Plugin.DataManager.GetExcelSheet<Race>().TryGetRow(raceId, out var row)
            ? GetGenderedName(row.Masculine.ToString(), row.Feminine.ToString(), sex)
            : "";
    }

    private static string GetTribeName(byte tribeId, byte sex)
    {
        return Plugin.DataManager.GetExcelSheet<Tribe>().TryGetRow(tribeId, out var row)
            ? GetGenderedName(row.Masculine.ToString(), row.Feminine.ToString(), sex)
            : "";
    }

    private static string GetGenderedName(string masculine, string feminine, byte sex)
    {
        return sex == 1 && !string.IsNullOrWhiteSpace(feminine)
            ? feminine
            : masculine;
    }

    private static string GetSexName(byte sex, UiLanguage language)
    {
        return sex == 1
            ? UiText.T(language, "Female", "女性")
            : UiText.T(language, "Male", "男性");
    }
}
