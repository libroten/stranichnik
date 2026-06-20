using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stranichnik.Diagnostics;

namespace Stranichnik.Sync.Credentials;

public sealed class ObfuscatedFileSyncCredentialStore
{
    private const int FormatVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly Action<string> _log;

    public ObfuscatedFileSyncCredentialStore(string filePath, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        _filePath = filePath;
        _log = log ?? Logs.Print;
    }

    public SyncCredentials? Load(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        try
        {
            if (!File.Exists(_filePath))
                return null;

            var json = File.ReadAllText(_filePath);
            var record = JsonSerializer.Deserialize<ObfuscatedCredentialRecord>(json);
            if (record is null ||
                record.FormatVersion != FormatVersion ||
                !string.Equals(record.UsernameHash, HashUsername(username), StringComparison.Ordinal))
            {
                return null;
            }

            var password = Deobfuscate(record.ObfuscatedPassword, username);
            if (string.IsNullOrWhiteSpace(password))
                return null;

            _log("Sync credentials loaded from obfuscated file fallback.");
            return new SyncCredentials(password);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            _log("Sync credentials file fallback load failed.");
            return null;
        }
    }

    public bool Save(string username, SyncCredentials credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(credentials);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? string.Empty);
            var record = new ObfuscatedCredentialRecord(
                FormatVersion,
                HashUsername(username),
                Obfuscate(credentials.Password, username));
            var json = JsonSerializer.Serialize(record, SerializerOptions);
            File.WriteAllText(_filePath, json);
            TryRestrictFilePermissions(_filePath);
            _log("Sync credentials saved to obfuscated file fallback.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log("Sync credentials file fallback save failed.");
            return false;
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _log("Sync credentials file fallback delete failed.");
        }
    }

    private static string Obfuscate(string password, string username)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var keyBytes = ExpandUsernameKey(username, passwordBytes.Length);
        var output = new byte[passwordBytes.Length];

        for (var index = 0; index < passwordBytes.Length; index++)
            output[index] = (byte)(passwordBytes[index] ^ keyBytes[index]);

        return Convert.ToBase64String(output);
    }

    private static string Deobfuscate(string obfuscatedPassword, string username)
    {
        var obfuscatedBytes = Convert.FromBase64String(obfuscatedPassword);
        var keyBytes = ExpandUsernameKey(username, obfuscatedBytes.Length);
        var output = new byte[obfuscatedBytes.Length];

        for (var index = 0; index < obfuscatedBytes.Length; index++)
            output[index] = (byte)(obfuscatedBytes[index] ^ keyBytes[index]);

        return Encoding.UTF8.GetString(output);
    }

    private static byte[] ExpandUsernameKey(string username, int length)
    {
        var usernameBytes = Encoding.UTF8.GetBytes(username);
        if (usernameBytes.Length == 0)
            throw new ArgumentException("Username must not be empty.", nameof(username));

        var keyBytes = new byte[length];
        for (var index = 0; index < length; index++)
            keyBytes[index] = usernameBytes[index % usernameBytes.Length];

        return keyBytes;
    }

    private static string HashUsername(string username)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        return Convert.ToHexString(hash);
    }

    private static void TryRestrictFilePermissions(string filePath)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }
    }

    private sealed record ObfuscatedCredentialRecord(
        int FormatVersion,
        string UsernameHash,
        string ObfuscatedPassword);
}
