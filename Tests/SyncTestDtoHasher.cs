using System;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;

namespace Stranichnik.Tests;

internal static class SyncTestDtoHasher
{
    public static T WithContentHash<T>(
        T value,
        SystemTextSyncJsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);

        return value switch
        {
            SyncItemDto item => (T)(object)WithContentHash(item, serializer),
            SyncIconAssetDto iconAsset => (T)(object)WithContentHash(iconAsset, serializer),
            SyncSecretIconAssetDto secretIconAsset =>
                (T)(object)WithContentHash(secretIconAsset, serializer),
            SyncCryptoProfileDto cryptoProfile => (T)(object)WithContentHash(cryptoProfile, serializer),
            SyncSecretResetEventDto resetEvent => (T)(object)WithContentHash(resetEvent, serializer),
            SyncDeviceInfoDto deviceInfo => (T)(object)WithContentHash(deviceInfo, serializer),
            _ => value
        };
    }

    private static SyncItemDto WithContentHash(
        SyncItemDto item,
        SystemTextSyncJsonSerializer serializer)
    {
        var valueWithPlaceholder = item with
        {
            ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder
        };
        return valueWithPlaceholder with
        {
            ContentHash = ComputeContentHash(valueWithPlaceholder, serializer)
        };
    }

    private static SyncIconAssetDto WithContentHash(
        SyncIconAssetDto iconAsset,
        SystemTextSyncJsonSerializer serializer)
    {
        var valueWithPlaceholder = iconAsset with
        {
            ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder
        };
        return valueWithPlaceholder with
        {
            ContentHash = ComputeContentHash(valueWithPlaceholder, serializer)
        };
    }

    private static SyncSecretIconAssetDto WithContentHash(
        SyncSecretIconAssetDto secretIconAsset,
        SystemTextSyncJsonSerializer serializer)
    {
        var valueWithPlaceholder = secretIconAsset with
        {
            ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder
        };
        return valueWithPlaceholder with
        {
            ContentHash = ComputeContentHash(valueWithPlaceholder, serializer)
        };
    }

    private static SyncCryptoProfileDto WithContentHash(
        SyncCryptoProfileDto cryptoProfile,
        SystemTextSyncJsonSerializer serializer)
    {
        var valueWithPlaceholder = cryptoProfile with
        {
            ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder
        };
        return valueWithPlaceholder with
        {
            ContentHash = ComputeContentHash(valueWithPlaceholder, serializer)
        };
    }

    private static SyncSecretResetEventDto WithContentHash(
        SyncSecretResetEventDto resetEvent,
        SystemTextSyncJsonSerializer serializer)
    {
        var valueWithPlaceholder = resetEvent with
        {
            ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder
        };
        return valueWithPlaceholder with
        {
            ContentHash = ComputeContentHash(valueWithPlaceholder, serializer)
        };
    }

    private static SyncDeviceInfoDto WithContentHash(
        SyncDeviceInfoDto deviceInfo,
        SystemTextSyncJsonSerializer serializer)
    {
        var valueWithPlaceholder = deviceInfo with
        {
            ContentHash = SyncRemoteObjectConstants.ContentHashPlaceholder
        };
        return valueWithPlaceholder with
        {
            ContentHash = ComputeContentHash(valueWithPlaceholder, serializer)
        };
    }

    private static string ComputeContentHash<T>(
        T valueWithPlaceholder,
        SystemTextSyncJsonSerializer serializer)
    {
        return new Sha256SyncContentHasher().ComputeHash(serializer.Serialize(valueWithPlaceholder));
    }
}
