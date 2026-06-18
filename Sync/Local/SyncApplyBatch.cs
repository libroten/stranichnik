using System.Collections.Generic;
using Stranichnik.Sync.Remote;

namespace Stranichnik.Sync.Local;

public sealed record SyncApplyBatch(
    IReadOnlyList<SyncAppliedRemoteObject<SyncSecretResetEventDto>> SecretResetEvents,
    IReadOnlyList<SyncAppliedRemoteObject<SyncCryptoProfileDto>> CryptoProfiles,
    IReadOnlyList<SyncAppliedRemoteObject<SyncIconAssetDto>> IconAssets,
    IReadOnlyList<SyncAppliedRemoteObject<SyncSecretIconAssetDto>> SecretIconAssets,
    IReadOnlyList<SyncAppliedRemoteObject<SyncItemDto>> Items)
{
    public static SyncApplyBatch Empty { get; } = new(
        [],
        [],
        [],
        [],
        []);
}
