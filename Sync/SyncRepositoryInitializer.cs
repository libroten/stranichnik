using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;

namespace Stranichnik.Sync;

public sealed class SyncRepositoryInitializer
{
    private readonly IWebDavSyncTransport _transport;
    private readonly ISyncJsonSerializer _serializer;
    private readonly SyncLocalIdentity _localIdentity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string> _repositoryIdFactory;

    public SyncRepositoryInitializer(
        IWebDavSyncTransport transport,
        ISyncJsonSerializer serializer,
        SyncLocalIdentity localIdentity,
        Func<DateTimeOffset>? clock = null,
        Func<string>? repositoryIdFactory = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(localIdentity);

        _transport = transport;
        _serializer = serializer;
        _localIdentity = localIdentity;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _repositoryIdFactory = repositoryIdFactory ?? (() => Guid.NewGuid().ToString("N"));
    }

    public async Task<SyncRepositoryInitializationResult> EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        await _transport.EnsureRepositoryAsync(cancellationToken).ConfigureAwait(false);

        var existingManifestBytes = await _transport
            .GetAsync(SyncRemoteRepositoryLayout.ManifestPath, cancellationToken)
            .ConfigureAwait(false);

        if (existingManifestBytes is null)
        {
            var createdManifest = CreateManifest();
            var putResult = await _transport.PutAsync(
                SyncRemoteRepositoryLayout.ManifestPath,
                _serializer.Serialize(createdManifest),
                expectedEtag: null,
                createOnly: true,
                cancellationToken).ConfigureAwait(false);

            if (putResult.Status == SyncPutStatus.CreatedOrUpdated)
                return SyncRepositoryInitializationResult.Ready(createdManifest);

            existingManifestBytes = await _transport
                .GetAsync(SyncRemoteRepositoryLayout.ManifestPath, cancellationToken)
                .ConfigureAwait(false);

            if (existingManifestBytes is null)
                return SyncRepositoryInitializationResult.InvalidRepository();
        }

        SyncManifestDto manifest;
        try
        {
            manifest = _serializer.Deserialize<SyncManifestDto>(existingManifestBytes);
        }
        catch (JsonException)
        {
            return SyncRepositoryInitializationResult.InvalidRepository();
        }

        if (manifest.MinimumAppSyncVersion > SyncRemoteObjectConstants.FormatVersion ||
            manifest.FormatVersion > SyncRemoteObjectConstants.FormatVersion)
        {
            return SyncRepositoryInitializationResult.UnsupportedRepositoryVersion(manifest);
        }

        try
        {
            SyncRemoteObjectValidator.Validate(manifest);
        }
        catch (SyncRemoteObjectValidationException)
        {
            return SyncRepositoryInitializationResult.InvalidRepository();
        }

        return SyncRepositoryInitializationResult.Ready(manifest);
    }

    private SyncManifestDto CreateManifest()
    {
        return new SyncManifestDto(
            SyncRemoteObjectConstants.ManifestSchema,
            SyncRemoteObjectConstants.FormatVersion,
            _repositoryIdFactory(),
            _clock(),
            _localIdentity.DeviceId,
            SyncRemoteObjectConstants.FormatVersion);
    }
}
