using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Sync.Remote;

public sealed class SyncRemoteObjectReader
{
    private readonly IWebDavSyncTransport _transport;
    private readonly ISyncJsonSerializer _serializer;
    private readonly ISyncContentHasher _contentHasher;

    public SyncRemoteObjectReader(
        IWebDavSyncTransport transport,
        ISyncJsonSerializer serializer,
        ISyncContentHasher? contentHasher = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(serializer);

        _transport = transport;
        _serializer = serializer;
        _contentHasher = contentHasher ?? new Sha256SyncContentHasher();
    }

    public async Task<IReadOnlyList<SyncRemoteReadResult<T>>> ReadObjectsAsync<T>(
        SyncObjectKind kind,
        CancellationToken cancellationToken)
    {
        var directory = SyncRemoteObjectPath.GetDirectory(kind);
        var remoteObjects = await _transport.ListAsync(directory, cancellationToken).ConfigureAwait(false);
        var results = new List<SyncRemoteReadResult<T>>(remoteObjects.Count);

        foreach (var remoteObject in remoteObjects)
        {
            results.Add(await ReadObjectAsync<T>(
                kind,
                remoteObject,
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<SyncRemoteReadResult<T>> ReadObjectAsync<T>(
        SyncObjectKind expectedKind,
        SyncRemoteObjectInfo remoteObject,
        CancellationToken cancellationToken)
    {
        if (!SyncRemoteObjectPath.TryParse(remoteObject.RelativePath, out var identity) ||
            identity is null ||
            identity.Kind != expectedKind)
        {
            return SyncRemoteReadResult.Failed<T>(SyncRemoteReadStatus.InvalidPath, remoteObject);
        }

        var bytes = await _transport.GetAsync(remoteObject.RelativePath, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
            return SyncRemoteReadResult.Failed<T>(SyncRemoteReadStatus.MissingContent, remoteObject, identity);

        T value;
        try
        {
            value = _serializer.Deserialize<T>(bytes);
        }
        catch (JsonException)
        {
            return SyncRemoteReadResult.Failed<T>(SyncRemoteReadStatus.InvalidJson, remoteObject, identity);
        }

        try
        {
            Validate(value);
            ValidateContentHash(value);
        }
        catch (SyncRemoteObjectValidationException)
        {
            return SyncRemoteReadResult.Failed<T>(SyncRemoteReadStatus.InvalidRemoteObject, remoteObject, identity);
        }

        return SyncRemoteReadResult.Success(remoteObject, identity, value);
    }

    private static void Validate<T>(T value)
    {
        switch (value)
        {
            case SyncItemDto item:
                SyncRemoteObjectValidator.Validate(item);
                break;
            case SyncIconAssetDto iconAsset:
                SyncRemoteObjectValidator.Validate(iconAsset);
                break;
            case SyncSecretIconAssetDto secretIconAsset:
                SyncRemoteObjectValidator.Validate(secretIconAsset);
                break;
            case SyncCryptoProfileDto cryptoProfile:
                SyncRemoteObjectValidator.Validate(cryptoProfile);
                break;
            case SyncSecretResetEventDto secretResetEvent:
                SyncRemoteObjectValidator.Validate(secretResetEvent);
                break;
            case SyncDeviceInfoDto deviceInfo:
                SyncRemoteObjectValidator.Validate(deviceInfo);
                break;
            case SyncManifestDto manifest:
                SyncRemoteObjectValidator.Validate(manifest);
                break;
            default:
                throw new SyncRemoteObjectValidationException("Remote object type is not supported.");
        }
    }

    private void ValidateContentHash<T>(T value)
    {
        switch (value)
        {
            case SyncItemDto item:
                ValidateContentHash(
                    item.ContentHash,
                    item with { ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder });
                break;
            case SyncIconAssetDto iconAsset:
                ValidateContentHash(
                    iconAsset.ContentHash,
                    iconAsset with { ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder });
                break;
            case SyncSecretIconAssetDto secretIconAsset:
                ValidateContentHash(
                    secretIconAsset.ContentHash,
                    secretIconAsset with { ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder });
                break;
            case SyncCryptoProfileDto cryptoProfile:
                ValidateContentHash(
                    cryptoProfile.ContentHash,
                    cryptoProfile with { ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder });
                break;
            case SyncSecretResetEventDto secretResetEvent:
                ValidateContentHash(
                    secretResetEvent.ContentHash,
                    secretResetEvent with { ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder });
                break;
            case SyncDeviceInfoDto deviceInfo:
                ValidateContentHash(
                    deviceInfo.ContentHash,
                    deviceInfo with { ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder });
                break;
        }
    }

    private void ValidateContentHash<T>(string remoteContentHash, T valueWithPlaceholder)
    {
        var canonicalJson = _serializer.Serialize(valueWithPlaceholder);
        var computedContentHash = _contentHasher.ComputeHash(canonicalJson);
        if (!string.Equals(remoteContentHash, computedContentHash, StringComparison.Ordinal))
            throw new SyncRemoteObjectValidationException("Remote object content hash does not match canonical content.");
    }
}
