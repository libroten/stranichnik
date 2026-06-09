using System;
using Stranichnik.Icons;
using Stranichnik.Security;
using Stranichnik.Storage;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SecretIconCryptoServiceTests
{
    [Fact]
    public void EncryptProcessedIconBytes_round_trips_processed_icon_bytes()
    {
        using var dataKey = CreateDataKey();

        var encrypted = SecretIconCryptoService.EncryptProcessedIconBytes(
            ProcessedIconBytes,
            dataKey,
            "secret-icon",
            "generation");
        var secretIcon = CreateSecretIcon("secret-icon", "generation", encrypted);

        var decrypted = SecretIconCryptoService.DecryptProcessedIconBytes(secretIcon, dataKey);

        Assert.Equal(ProcessedIconBytes, decrypted);
    }

    [Fact]
    public void DecryptProcessedIconBytes_rejects_payload_bound_to_different_asset()
    {
        using var dataKey = CreateDataKey();
        var encrypted = SecretIconCryptoService.EncryptProcessedIconBytes(
            ProcessedIconBytes,
            dataKey,
            "secret-icon",
            "generation");
        var secretIcon = CreateSecretIcon("other-secret-icon", "generation", encrypted);

        Assert.Throws<SecretPayloadException>(() =>
            SecretIconCryptoService.DecryptProcessedIconBytes(secretIcon, dataKey));
    }

    [Fact]
    public void DecryptProcessedIconBytes_rejects_payload_bound_to_different_generation()
    {
        using var dataKey = CreateDataKey();
        var encrypted = SecretIconCryptoService.EncryptProcessedIconBytes(
            ProcessedIconBytes,
            dataKey,
            "secret-icon",
            "generation");
        var secretIcon = CreateSecretIcon("secret-icon", "other-generation", encrypted);

        Assert.Throws<SecretPayloadException>(() =>
            SecretIconCryptoService.DecryptProcessedIconBytes(secretIcon, dataKey));
    }

    private static RuntimeSecretKey CreateDataKey()
    {
        return new RuntimeSecretKey(DataKeyBytes);
    }

    private static SecretIconAssetRecord CreateSecretIcon(
        string id,
        string secretGenerationId,
        EncryptedSecretIconPayloadRecord encryptedPayload)
    {
        return new(
            id,
            "sha256",
            "source-hash",
            SourceSizeBytes: 3,
            "image/png",
            ProcessedWidth: 64,
            ProcessedHeight: 64,
            encryptedPayload,
            secretGenerationId,
            CreatedAt);
    }

    private static readonly byte[] DataKeyBytes =
    [
        1, 2, 3, 4, 5, 6, 7, 8,
        9, 10, 11, 12, 13, 14, 15, 16,
        17, 18, 19, 20, 21, 22, 23, 24,
        25, 26, 27, 28, 29, 30, 31, 32
    ];

    private static readonly byte[] ProcessedIconBytes = [10, 20, 30, 40];

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
