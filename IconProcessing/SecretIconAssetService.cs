using System;
using Stranichnik.Diagnostics;
using Stranichnik.Security;
using Stranichnik.Storage;

namespace Stranichnik.Icons;

public sealed class SecretIconAssetService
{
    private readonly IBookmarkTreeStore _treeStore;
    private readonly IIconImageProcessor _imageProcessor;
    private readonly Func<string> _idFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly IconProcessingOptions _options;

    public SecretIconAssetService(
        IBookmarkTreeStore treeStore,
        IIconImageProcessor imageProcessor,
        IconProcessingOptions? options = null,
        Func<string>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _treeStore = treeStore;
        _imageProcessor = imageProcessor;
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _options = options ?? new IconProcessingOptions();
    }

    public SecretIconAssetRecord GetOrCreateFromOriginalBytes(
        ReadOnlyMemory<byte> originalBytes,
        RuntimeSecretKey dataKey,
        string secretGenerationId)
    {
        ValidateOriginalBytes(originalBytes);
        ArgumentNullException.ThrowIfNull(dataKey);
        ValidateSecretGenerationId(secretGenerationId);

        var sourceHash = IconHash.ComputeSha256(originalBytes.Span);
        var existing = _treeStore.GetSecretIconAssetBySourceHash(IconHash.Sha256Algorithm, sourceHash);

        if (existing is not null)
        {
            Logs.Print("Secret icon asset reused from storage by source hash.");
            return existing;
        }

        Logs.Print($"Secret icon image processing started. SourceBytes={originalBytes.Length}.");
        var processed = _imageProcessor.Process(originalBytes);
        Logs.Print(
            $"Secret icon image processing completed. OutputMime={processed.MimeType}, Width={processed.Width}, Height={processed.Height}, OutputBytes={processed.Bytes.Length}.");

        return GetOrCreateFromProcessedBytes(
            IconHash.Sha256Algorithm,
            sourceHash,
            originalBytes.Length,
            processed,
            dataKey,
            secretGenerationId);
    }

    public SecretIconAssetRecord? GetOrCreateFromRegularIconAsset(
        string? iconAssetId,
        RuntimeSecretKey dataKey,
        string secretGenerationId)
    {
        if (iconAssetId is null)
            return null;

        ArgumentNullException.ThrowIfNull(dataKey);
        ValidateSecretGenerationId(secretGenerationId);

        var regularIcon = _treeStore.GetIconAsset(iconAssetId);
        if (regularIcon is null)
        {
            Logs.Print("Secret icon conversion skipped: regular icon asset was not found.");
            return null;
        }

        var existing = _treeStore.GetSecretIconAssetBySourceHash(
            regularIcon.SourceHashAlgorithm,
            regularIcon.SourceHash);

        if (existing is not null)
        {
            Logs.Print("Secret icon conversion reused encrypted asset by source hash.");
            return existing;
        }

        var processed = new ProcessedIconImage(
            regularIcon.ProcessedMimeType,
            regularIcon.ProcessedWidth,
            regularIcon.ProcessedHeight,
            regularIcon.ProcessedBytes);

        Logs.Print("Secret icon conversion started from regular icon asset.");
        return GetOrCreateFromProcessedBytes(
            regularIcon.SourceHashAlgorithm,
            regularIcon.SourceHash,
            regularIcon.SourceSizeBytes,
            processed,
            dataKey,
            secretGenerationId);
    }

    public BookmarkIconAssetRecord? GetOrCreateRegularFromSecretIconAsset(
        string? secretIconAssetId,
        RuntimeSecretKey dataKey)
    {
        if (secretIconAssetId is null)
            return null;

        ArgumentNullException.ThrowIfNull(dataKey);

        var secretIcon = _treeStore.GetSecretIconAsset(secretIconAssetId);
        if (secretIcon is null)
        {
            Logs.Print("Regular icon conversion skipped: secret icon asset was not found.");
            return null;
        }

        var existing = _treeStore.GetIconAssetBySourceHash(
            secretIcon.SourceHashAlgorithm,
            secretIcon.SourceHash);

        if (existing is not null)
        {
            Logs.Print("Regular icon conversion reused plaintext asset by source hash.");
            return existing;
        }

        Logs.Print("Regular icon conversion started from secret icon asset.");
        var processedBytes = SecretIconCryptoService.DecryptProcessedIconBytes(secretIcon, dataKey);
        var record = new BookmarkIconAssetRecord(
            _idFactory(),
            secretIcon.SourceHashAlgorithm,
            secretIcon.SourceHash,
            secretIcon.SourceSizeBytes,
            secretIcon.ProcessedMimeType,
            secretIcon.ProcessedWidth,
            secretIcon.ProcessedHeight,
            processedBytes,
            _clock());

        var stored = _treeStore.GetOrCreateIconAsset(record);
        Logs.Print(stored.Id == record.Id
            ? "Regular icon asset created from decrypted secret icon."
            : "Regular icon asset reused after secret icon decryption.");
        return stored;
    }

    private SecretIconAssetRecord GetOrCreateFromProcessedBytes(
        string sourceHashAlgorithm,
        string sourceHash,
        long sourceSizeBytes,
        ProcessedIconImage processed,
        RuntimeSecretKey dataKey,
        string secretGenerationId)
    {
        var recordId = _idFactory();
        var encryptedPayload = SecretIconCryptoService.EncryptProcessedIconBytes(
            processed.Bytes,
            dataKey,
            recordId,
            secretGenerationId);

        var record = new SecretIconAssetRecord(
            recordId,
            sourceHashAlgorithm,
            sourceHash,
            sourceSizeBytes,
            processed.MimeType,
            processed.Width,
            processed.Height,
            encryptedPayload,
            secretGenerationId,
            _clock());

        var stored = _treeStore.GetOrCreateSecretIconAsset(record);
        Logs.Print(stored.Id == record.Id
            ? "Secret icon asset created in storage."
            : "Secret icon asset reused from storage after encryption.");
        return stored;
    }

    private void ValidateOriginalBytes(ReadOnlyMemory<byte> originalBytes)
    {
        if (originalBytes.Length == 0)
        {
            Logs.Print("Secret icon asset creation rejected: empty input.");
            throw new ArgumentException("Icon image bytes cannot be empty.", nameof(originalBytes));
        }

        if (originalBytes.Length > _options.MaxInputBytes)
        {
            Logs.Print(
                $"Secret icon asset creation rejected: input is too large. Bytes={originalBytes.Length}, MaxBytes={_options.MaxInputBytes}.");
            throw new InvalidOperationException("Icon image is too large.");
        }
    }

    private static void ValidateSecretGenerationId(string secretGenerationId)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID must not be empty.", nameof(secretGenerationId));
    }
}
