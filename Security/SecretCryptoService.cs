using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stranichnik.Storage;

namespace Stranichnik.Security;

public sealed class SecretCryptoService : ISecretCryptoService
{
    private const string PasswordCheckPlaintext = "stranichnik-password-check-v1";
    private readonly int _defaultPbkdf2Iterations;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SecretCryptoService()
        : this(SecretEncryptionConstants.DefaultPbkdf2Iterations)
    {
    }

    public SecretCryptoService(int defaultPbkdf2Iterations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultPbkdf2Iterations);
        _defaultPbkdf2Iterations = defaultPbkdf2Iterations;
    }

    public CryptoProfileCreationResult CreateProfile(
        string masterPassword,
        DateTimeOffset nowUtc)
    {
        ValidatePassword(masterPassword, nameof(masterPassword));
        EnsureAesGcmSupported();

        var dataKey = RandomNumberGenerator.GetBytes(SecretEncryptionConstants.DataKeyLengthBytes);
        var salt = RandomNumberGenerator.GetBytes(SecretEncryptionConstants.KdfSaltLengthBytes);
        var kek = DeriveKek(masterPassword, salt, _defaultPbkdf2Iterations);

        try
        {
            var wrappedDataKeyNonce = CreateNonce();
            var wrappedDataKey = EncryptBytes(
                dataKey,
                kek,
                wrappedDataKeyNonce,
                BuildWrappedDataKeyAad(SecretCryptoProfileIds.ActiveProfileId));

            var passwordCheckNonce = CreateNonce();
            var passwordCheckPayload = EncryptBytes(
                Encoding.UTF8.GetBytes(PasswordCheckPlaintext),
                kek,
                passwordCheckNonce,
                BuildPasswordCheckAad(SecretCryptoProfileIds.ActiveProfileId));

            var profile = new CryptoProfileRecord(
                SecretCryptoProfileIds.ActiveProfileId,
                SecretEncryptionConstants.CurrentProfileVersion,
                SecretEncryptionConstants.KdfName,
                SecretEncryptionConstants.KdfHashAlgorithm,
                _defaultPbkdf2Iterations,
                salt,
                SecretEncryptionConstants.KekLengthBytes,
                SecretEncryptionConstants.DataKeyAlgorithm,
                wrappedDataKey,
                wrappedDataKeyNonce,
                SecretEncryptionConstants.EncryptionAlgorithm,
                SecretEncryptionConstants.PayloadFormat,
                passwordCheckPayload,
                passwordCheckNonce,
                nowUtc,
                nowUtc);

            return new CryptoProfileCreationResult(profile, new RuntimeSecretKey(dataKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public SecretUnlockResult Unlock(
        CryptoProfileRecord profile,
        string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidatePassword(masterPassword, nameof(masterPassword));

        if (!IsSupportedProfile(profile) || !AesGcm.IsSupported)
            return SecretUnlockResult.Failed(SecretCryptoFailureReason.UnsupportedProfile);

        var kek = DeriveKek(masterPassword, profile.KdfSalt.Span, profile.KdfIterations);

        try
        {
            var passwordCheckBytes = DecryptBytes(
                profile.PasswordCheckPayload.Span,
                kek,
                profile.PasswordCheckNonce.Span,
                BuildPasswordCheckAad(profile.Id));

            var passwordCheck = Encoding.UTF8.GetString(passwordCheckBytes);
            CryptographicOperations.ZeroMemory(passwordCheckBytes);

            if (!string.Equals(passwordCheck, PasswordCheckPlaintext, StringComparison.Ordinal))
                return SecretUnlockResult.Failed(SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile);

            var dataKey = DecryptBytes(
                profile.WrappedDataKey.Span,
                kek,
                profile.WrappedDataKeyNonce.Span,
                BuildWrappedDataKeyAad(profile.Id));

            try
            {
                return SecretUnlockResult.Succeeded(new RuntimeSecretKey(dataKey));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dataKey);
            }
        }
        catch (CryptographicException)
        {
            return SecretUnlockResult.Failed(SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile);
        }
        catch (FormatException)
        {
            return SecretUnlockResult.Failed(SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile);
        }
        catch (JsonException)
        {
            return SecretUnlockResult.Failed(SecretCryptoFailureReason.InvalidPasswordOrCorruptProfile);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public EncryptedSecretPayload EncryptBookmarkPayload(
        SecretBookmarkPayloadV1 payload,
        RuntimeSecretKey dataKey,
        long cryptoProfileId,
        string itemId)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(dataKey);
        ValidateItemId(itemId);
        EnsureAesGcmSupported();

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var nonce = CreateNonce();

        try
        {
            var encryptedPayload = EncryptBytes(
                plaintext,
                dataKey.Span,
                nonce,
                BuildBookmarkPayloadAad(itemId, cryptoProfileId));

            return new EncryptedSecretPayload(
                encryptedPayload,
                nonce,
                cryptoProfileId,
                SecretEncryptionConstants.CurrentPayloadFormatVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public SecretBookmarkPayloadV1 DecryptBookmarkPayload(
        EncryptedBookmarkPayloadRecord encryptedPayload,
        RuntimeSecretKey dataKey,
        string itemId)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);
        ArgumentNullException.ThrowIfNull(dataKey);
        ValidateItemId(itemId);

        try
        {
            var plaintext = DecryptBytes(
                encryptedPayload.Payload.Span,
                dataKey.Span,
                encryptedPayload.Nonce.Span,
                BuildBookmarkPayloadAad(itemId, encryptedPayload.CryptoProfileId));

            try
            {
                return JsonSerializer.Deserialize<SecretBookmarkPayloadV1>(plaintext, JsonOptions)
                    ?? throw new CryptographicException("Secret bookmark payload is empty.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException ex)
        {
            throw new SecretPayloadException(SecretCryptoFailureReason.InvalidPayload, ex);
        }
        catch (JsonException ex)
        {
            throw new SecretPayloadException(SecretCryptoFailureReason.InvalidPayload, ex);
        }
        catch (FormatException ex)
        {
            throw new SecretPayloadException(SecretCryptoFailureReason.InvalidPayload, ex);
        }
    }

    public CryptoProfileRecord ChangeMasterPassword(
        CryptoProfileRecord profile,
        RuntimeSecretKey dataKey,
        string newMasterPassword,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(dataKey);
        ValidatePassword(newMasterPassword, nameof(newMasterPassword));

        if (!IsSupportedProfile(profile))
            throw new SecretPayloadException(SecretCryptoFailureReason.UnsupportedProfile);

        var salt = RandomNumberGenerator.GetBytes(SecretEncryptionConstants.KdfSaltLengthBytes);
        var kek = DeriveKek(newMasterPassword, salt, profile.KdfIterations);

        try
        {
            var wrappedDataKeyNonce = CreateNonce();
            var wrappedDataKey = EncryptBytes(
                dataKey.Span,
                kek,
                wrappedDataKeyNonce,
                BuildWrappedDataKeyAad(profile.Id));

            var passwordCheckNonce = CreateNonce();
            var passwordCheckPayload = EncryptBytes(
                Encoding.UTF8.GetBytes(PasswordCheckPlaintext),
                kek,
                passwordCheckNonce,
                BuildPasswordCheckAad(profile.Id));

            return profile with
            {
                KdfSalt = salt,
                WrappedDataKey = wrappedDataKey,
                WrappedDataKeyNonce = wrappedDataKeyNonce,
                PasswordCheckPayload = passwordCheckPayload,
                PasswordCheckNonce = passwordCheckNonce,
                UpdatedAtUtc = nowUtc
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private static byte[] DeriveKek(
        string password,
        ReadOnlySpan<byte> salt,
        int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            SecretEncryptionConstants.KekLengthBytes);
    }

    private static byte[] EncryptBytes(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> aad)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[SecretEncryptionConstants.AesGcmTagLengthBytes];

        using var aes = new AesGcm(key, SecretEncryptionConstants.AesGcmTagLengthBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

        var envelope = new SecretPayloadEnvelope(
            SecretEncryptionConstants.CurrentPayloadFormatVersion,
            SecretEncryptionConstants.EncryptionAlgorithm,
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext));

        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    private static byte[] DecryptBytes(
        ReadOnlySpan<byte> encryptedEnvelope,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> aad)
    {
        var envelope = JsonSerializer.Deserialize<SecretPayloadEnvelope>(encryptedEnvelope, JsonOptions)
            ?? throw new CryptographicException("Encrypted payload envelope is empty.");

        if (envelope.Version != SecretEncryptionConstants.CurrentPayloadFormatVersion ||
            !string.Equals(envelope.Algorithm, SecretEncryptionConstants.EncryptionAlgorithm, StringComparison.Ordinal))
        {
            throw new CryptographicException("Encrypted payload envelope is unsupported.");
        }

        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, SecretEncryptionConstants.AesGcmTagLengthBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

        return plaintext;
    }

    private static byte[] CreateNonce()
    {
        return RandomNumberGenerator.GetBytes(SecretEncryptionConstants.AesGcmNonceLengthBytes);
    }

    private static byte[] BuildWrappedDataKeyAad(long profileId)
    {
        return Encoding.UTF8.GetBytes($"stranichnik:wrapped-dek:v1:profile:{profileId}");
    }

    private static byte[] BuildPasswordCheckAad(long profileId)
    {
        return Encoding.UTF8.GetBytes($"stranichnik:password-check:v1:profile:{profileId}");
    }

    private static byte[] BuildBookmarkPayloadAad(string itemId, long profileId)
    {
        return Encoding.UTF8.GetBytes($"stranichnik:bookmark-secret:v1:item:{itemId}:profile:{profileId}");
    }

    private static bool IsSupportedProfile(CryptoProfileRecord profile)
    {
        return profile.ProfileVersion == SecretEncryptionConstants.CurrentProfileVersion &&
            string.Equals(profile.KdfName, SecretEncryptionConstants.KdfName, StringComparison.Ordinal) &&
            string.Equals(profile.KdfHashAlgorithm, SecretEncryptionConstants.KdfHashAlgorithm, StringComparison.Ordinal) &&
            profile.KdfIterations > 0 &&
            profile.KdfSalt.Length == SecretEncryptionConstants.KdfSaltLengthBytes &&
            profile.KekLengthBytes == SecretEncryptionConstants.KekLengthBytes &&
            string.Equals(profile.DataKeyAlgorithm, SecretEncryptionConstants.DataKeyAlgorithm, StringComparison.Ordinal) &&
            string.Equals(profile.EncryptionAlgorithm, SecretEncryptionConstants.EncryptionAlgorithm, StringComparison.Ordinal) &&
            string.Equals(profile.PayloadFormat, SecretEncryptionConstants.PayloadFormat, StringComparison.Ordinal);
    }

    private static void ValidatePassword(string password, string parameterName)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password must not be empty.", parameterName);
    }

    private static void ValidateItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("Item ID must not be empty.", nameof(itemId));
    }

    private static void EnsureAesGcmSupported()
    {
        if (!AesGcm.IsSupported)
            throw new PlatformNotSupportedException("AES-GCM is not supported on this platform.");
    }
}
