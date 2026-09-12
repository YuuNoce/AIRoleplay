using System;
using System.IO;
using System.Text;

namespace AIRoleplay;

public enum CharacterProfileFileError
{
    None,
    NotLoggedIn,
    NotFound,
    TooLong,
    InvalidEncoding,
    ReadFailed,
    WriteFailed,
}

public sealed class CharacterProfileStore
{
    public const string ProfileFileName = "profile.txt";
    private const int MaxProfileFileBytes = Configuration.MaxCharacterPromptLength * 4 + 3;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public string CharactersDirectory =>
        Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "characters");

    public string? GetCurrentCharacterDirectory()
    {
        var character = AIRoleplayLogger.GetCurrentCharacterFolder();
        return character == null ? null : Path.Combine(CharactersDirectory, character);
    }

    public string? GetCurrentProfilePath()
    {
        var directory = GetCurrentCharacterDirectory();
        return directory == null ? null : Path.Combine(directory, ProfileFileName);
    }

    public bool TryReadCurrent(out string profile, out CharacterProfileFileError error)
    {
        var path = GetCurrentProfilePath();
        if (path == null)
        {
            profile = "";
            error = CharacterProfileFileError.NotLoggedIn;
            return false;
        }

        return TryReadFile(path, out profile, out error);
    }

    public bool TryReadFile(string path, out string profile, out CharacterProfileFileError error)
    {
        profile = "";
        if (!File.Exists(path))
        {
            error = CharacterProfileFileError.NotFound;
            return false;
        }

        try
        {
            if (new FileInfo(path).Length > MaxProfileFileBytes)
            {
                error = CharacterProfileFileError.TooLong;
                return false;
            }

            var bytes = File.ReadAllBytes(path);
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            var value = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            if (value.Length > Configuration.MaxCharacterPromptLength)
            {
                error = CharacterProfileFileError.TooLong;
                return false;
            }

            profile = value;
            error = CharacterProfileFileError.None;
            return true;
        }
        catch (DecoderFallbackException ex)
        {
            Plugin.Log.Warning(ex, "Character profile file is not valid UTF-8. Path={Path}", path);
            error = CharacterProfileFileError.InvalidEncoding;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Plugin.Log.Warning(ex, "Failed to read character profile file. Path={Path}", path);
            error = CharacterProfileFileError.ReadFailed;
            return false;
        }
    }

    public bool TryWriteCurrent(string profile, out CharacterProfileFileError error)
    {
        var path = GetCurrentProfilePath();
        if (path == null)
        {
            error = CharacterProfileFileError.NotLoggedIn;
            return false;
        }

        if (profile.Length > Configuration.MaxCharacterPromptLength)
        {
            error = CharacterProfileFileError.TooLong;
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, profile, StrictUtf8);
            error = CharacterProfileFileError.None;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Plugin.Log.Warning(ex, "Failed to write character profile file. Path={Path}", path);
            error = CharacterProfileFileError.WriteFailed;
            return false;
        }
    }
}
