using System;
using System.Runtime.InteropServices;

namespace AIRoleplay;

public sealed class WindowsCredentialApiKeyReader
{
    private const int CredTypeGeneric = 1;
    private const int ErrorNotFound = 1168;

    public string GetCredentialTarget(LlmProvider provider) =>
        provider switch
        {
            LlmProvider.OpenAI => "AIRoleplay/OpenAI",
            LlmProvider.Claude => "AIRoleplay/Claude",
            LlmProvider.Gemini => "AIRoleplay/Gemini",
            LlmProvider.DeepSeek => "AIRoleplay/DeepSeek",
            LlmProvider.Zai => "AIRoleplay/Zai",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };

    public bool HasApiKey(LlmProvider provider)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var target = GetCredentialTarget(provider);
        if (!CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
        {
            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            return credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public bool TryReadApiKey(LlmProvider provider, out string apiKey, out string? error)
    {
        apiKey = "";
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "Windows Credential Manager is only available on Windows.";
            return false;
        }

        var target = GetCredentialTarget(provider);
        if (!CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
        {
            var errorCode = Marshal.GetLastWin32Error();
            if (errorCode != ErrorNotFound)
            {
                error = $"Failed to read Windows credential {target}. Win32Error={errorCode}";
            }

            return false;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
            {
                return false;
            }

            apiKey = Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    credential.CredentialBlobSize / sizeof(char))
                ?.TrimEnd('\0')
                ?? "";
            return !string.IsNullOrWhiteSpace(apiKey);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
