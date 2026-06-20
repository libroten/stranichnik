using System;
using System.Runtime.InteropServices;
using System.Text;
using Stranichnik.Diagnostics;

namespace Stranichnik.Sync.Credentials;

public sealed class MacOsKeychainSyncCredentialStore : ISystemSyncCredentialStore
{
    private const string ServiceName = "Stranichnik WebDAV";
    private readonly Action<string> _log;

    public MacOsKeychainSyncCredentialStore(Action<string>? log = null)
    {
        _log = log ?? Logs.Print;
    }

    public SyncCredentials? Load(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (!OperatingSystem.IsMacOS())
            return null;

        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(username);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)accountBytes.Length,
            accountBytes,
            out var passwordLength,
            out var passwordData,
            out var itemRef);

        if (status != 0)
            return null;

        if (passwordData == IntPtr.Zero || passwordLength == 0)
        {
            if (itemRef != IntPtr.Zero)
                CFRelease(itemRef);

            return null;
        }

        try
        {
            var passwordBytes = new byte[(int)passwordLength];
            Marshal.Copy(passwordData, passwordBytes, 0, (int)passwordLength);
            var password = Encoding.UTF8.GetString(passwordBytes);
            if (string.IsNullOrWhiteSpace(password))
                return null;

            _log("Sync credentials loaded from macOS Keychain.");
            return new SyncCredentials(password);
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            if (itemRef != IntPtr.Zero)
                CFRelease(itemRef);
        }
    }

    public bool Save(string username, SyncCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!OperatingSystem.IsMacOS())
            return false;

        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(username);
        var passwordBytes = Encoding.UTF8.GetBytes(credentials.Password);
        var findStatus = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)accountBytes.Length,
            accountBytes,
            out _,
            out var passwordData,
            out var itemRef);

        if (passwordData != IntPtr.Zero)
            _ = SecKeychainItemFreeContent(IntPtr.Zero, passwordData);

        if (findStatus == 0 && itemRef != IntPtr.Zero)
        {
            try
            {
                return SecKeychainItemModifyAttributesAndData(
                    itemRef,
                    IntPtr.Zero,
                    (uint)passwordBytes.Length,
                    passwordBytes) == 0;
            }
            finally
            {
                CFRelease(itemRef);
            }
        }

        var addStatus = SecKeychainAddGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)accountBytes.Length,
            accountBytes,
            (uint)passwordBytes.Length,
            passwordBytes,
            out itemRef);

        if (itemRef != IntPtr.Zero)
            CFRelease(itemRef);

        return addStatus == 0;
    }

    public void Delete(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        if (!OperatingSystem.IsMacOS())
            return;

        var serviceBytes = Encoding.UTF8.GetBytes(ServiceName);
        var accountBytes = Encoding.UTF8.GetBytes(username);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            (uint)serviceBytes.Length,
            serviceBytes,
            (uint)accountBytes.Length,
            accountBytes,
            out _,
            out var passwordData,
            out var itemRef);

        if (passwordData != IntPtr.Zero)
            _ = SecKeychainItemFreeContent(IntPtr.Zero, passwordData);

        if (status != 0 || itemRef == IntPtr.Zero)
            return;

        try
        {
            _ = SecKeychainItemDelete(itemRef);
        }
        finally
        {
            CFRelease(itemRef);
        }
    }

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(
        IntPtr itemRef,
        IntPtr attrList,
        uint length,
        byte[] data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}
