using System;
using System.Collections.Generic;
using System.Linq;

namespace Stranichnik.Storage;

public sealed class InMemorySecretResetStore : ISecretResetStore
{
    private readonly InMemoryBookmarkTreeStore _treeStore;
    private readonly InMemorySecretProfileStore _profileStore;
    private readonly Func<string> _idFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _resetDeviceId;
    private readonly List<SecretResetEventRecord> _resetEvents = [];

    public InMemorySecretResetStore(
        InMemoryBookmarkTreeStore treeStore,
        InMemorySecretProfileStore profileStore,
        Func<string>? idFactory = null,
        Func<DateTimeOffset>? clock = null,
        string? resetDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(treeStore);
        ArgumentNullException.ThrowIfNull(profileStore);

        _treeStore = treeStore;
        _profileStore = profileStore;
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _resetDeviceId = string.IsNullOrWhiteSpace(resetDeviceId)
            ? "local"
            : resetDeviceId;
    }

    public SecretResetStoreResult ResetMasterPasswordAndPurgeSecrets(string secretGenerationId)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID must not be empty.", nameof(secretGenerationId));

        var profile = _profileStore.LoadActiveProfile();
        if (profile is null || !string.Equals(profile.SecretGenerationId, secretGenerationId, StringComparison.Ordinal))
            throw new InvalidOperationException("Secret crypto profile was not found.");

        if (_resetEvents.Any(resetEvent =>
                string.Equals(resetEvent.SecretGenerationId, secretGenerationId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Secret generation has already been reset.");
        }

        var now = _clock();
        _resetEvents.Add(new SecretResetEventRecord(
            _idFactory(),
            secretGenerationId,
            now,
            _resetDeviceId,
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null));

        var purgedCount = _treeStore.PurgeSecretBookmarksForMasterPasswordReset(secretGenerationId);
        if (!_profileStore.DeleteActiveProfileForMasterPasswordReset(secretGenerationId))
            throw new InvalidOperationException("Secret crypto profile was not found.");

        return new SecretResetStoreResult(purgedCount);
    }

    public IReadOnlyList<SecretResetEventRecord> LoadResetEvents()
    {
        return _resetEvents.ToList();
    }
}
