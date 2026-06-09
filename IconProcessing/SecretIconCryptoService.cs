using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stranichnik.Security;
using Stranichnik.Storage;

namespace Stranichnik.Icons;

public static class SecretIconCryptoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static EncryptedSecretIconPayloadRecord EncryptProcessedIconBytes(
        ReadOnlyMemory<byte> processedBytes,
        RuntimeSecretKey dataKey,
        string secretIconAssetId,
        string secretGenerationId)
    {
        if (processedBytes.IsEmpty)
            throw new ArgumentException("Processed icon bytes cannot be empty.", nameof(processedBytes));

        ArgumentNullException.ThrowIfNull(dataKey);
        ValidateSecretIconAssetId(secretIconAssetId);
        ValidateSecretGenerationId(secretGenerationId);
        EnsureAesGcmSupported();

        var nonce = RandomNumberGenerator.GetBytes(SecretEncryptionConstants.AesGcmNonceLengthBytes);
        var encryptedPayload = EncryptBytes(
            processedBytes.Span,
            dataKey.Span,
            nonce,
            BuildSecretIconAad(secretIconAssetId, secretGenerationId));

        return new EncryptedSecretIconPayloadRecord(
            encryptedPayload,
            nonce,
            SecretEncryptionConstants.CurrentPayloadFormatVersion);
    }

    public static byte[] DecryptProcessedIconBytes(
        SecretIconAssetRecord secretIconAsset,
        RuntimeSecretKey dataKey)
    {
        ArgumentNullException.ThrowIfNull(secretIconAsset);
        ArgumentNullException.ThrowIfNull(dataKey);
        ValidateSecretIconAssetId(secretIconAsset.Id);
        ValidateSecretGenerationId(secretIconAsset.SecretGenerationId);

        try
        {
            return DecryptBytes(
                secretIconAsset.EncryptedProcessedBytes.Payload.Span,
                dataKey.Span,
                secretIconAsset.EncryptedProcessedBytes.Nonce.Span,
                BuildSecretIconAad(secretIconAsset.Id, secretIconAsset.SecretGenerationId));
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

        var envelope = new SecretIconPayloadEnvelope(
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
        var envelope = JsonSerializer.Deserialize<SecretIconPayloadEnvelope>(encryptedEnvelope, JsonOptions)
            ?? throw new CryptographicException("Encrypted secret icon payload envelope is empty.");

        if (envelope.Version != SecretEncryptionConstants.CurrentPayloadFormatVersion ||
            !string.Equals(envelope.Algorithm, SecretEncryptionConstants.EncryptionAlgorithm, StringComparison.Ordinal))
        {
            throw new CryptographicException("Encrypted secret icon payload envelope is unsupported.");
        }

        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, SecretEncryptionConstants.AesGcmTagLengthBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, aad);

        return plaintext;
    }

    private static byte[] BuildSecretIconAad(string secretIconAssetId, string secretGenerationId)
    {
        return Encoding.UTF8.GetBytes(
            $"stranichnik:secret-icon:v1:asset:{secretIconAssetId}:generation:{secretGenerationId}");
    }

    private static void ValidateSecretIconAssetId(string secretIconAssetId)
    {
        if (string.IsNullOrWhiteSpace(secretIconAssetId))
            throw new ArgumentException("Secret icon asset ID must not be empty.", nameof(secretIconAssetId));
    }

    private static void ValidateSecretGenerationId(string secretGenerationId)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID must not be empty.", nameof(secretGenerationId));
    }

    private static void EnsureAesGcmSupported()
    {
        if (!AesGcm.IsSupported)
            throw new PlatformNotSupportedException("AES-GCM is not supported on this platform.");
    }

    private sealed record SecretIconPayloadEnvelope(
        int Version,
        string Algorithm,
        string Tag,
        string Ciphertext);
}
