using System;
using System.Runtime.InteropServices;
using System.Text;
using Stranichnik.Diagnostics;

namespace Stranichnik.Sync.Credentials;

public sealed class WindowsCredentialManagerSyncCredentialStore : ISystemSyncCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistenceLocalMachine = 2;
    private const string TargetName = "Stranichnik.WebDAV";
    private readonly Action<string> _log;

    public WindowsCredentialManagerSyncCredentialStore(Action<string>? log = null)
    {
        _log = log ?? Logs.Print;
    }

    public SyncCredentials? Load(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (!OperatingSystem.IsWindows())
            return null;

        if (!CredRead(TargetName, CredentialTypeGeneric, 0, out var credentialPointer))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            var savedUsername = credential.UserName ?? string.Empty;
            if (!string.Equals(savedUsername, username, StringComparison.Ordinal))
                return null;

            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return null;

            var passwordBytes = new byte[(int)credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, (int)credential.CredentialBlobSize);
            var password = Encoding.Unicode.GetString(passwordBytes);
            if (string.IsNullOrWhiteSpace(password))
                return null;

            _log("Sync credentials loaded from Windows Credential Manager.");
            return new SyncCredentials(password);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public bool Save(string username, SyncCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!OperatingSystem.IsWindows())
            return false;

        var passwordBytes = Encoding.Unicode.GetBytes(credentials.Password);
        var credential = new Credential
        {
            Type = CredentialTypeGeneric,
            TargetName = TargetName,
            CredentialBlobSize = (uint)passwordBytes.Length,
            Persist = CredentialPersistenceLocalMachine,
            UserName = username
        };

        var blobHandle = GCHandle.Alloc(passwordBytes, GCHandleType.Pinned);
        try
        {
            credential.CredentialBlob = blobHandle.AddrOfPinnedObject();
            return CredWrite(ref credential, 0);
        }
        finally
        {
            blobHandle.Free();
        }
    }

    public void Delete(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (OperatingSystem.IsWindows())
            CredDelete(TargetName, CredentialTypeGeneric, 0);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(
        string target,
        int type,
        int reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential userCredential, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
