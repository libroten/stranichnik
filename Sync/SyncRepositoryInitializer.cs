using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Diagnostics;
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
    private readonly Action<string> _log;

    public SyncRepositoryInitializer(
        IWebDavSyncTransport transport,
        ISyncJsonSerializer serializer,
        SyncLocalIdentity localIdentity,
        Func<DateTimeOffset>? clock = null,
        Func<string>? repositoryIdFactory = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(localIdentity);

        _transport = transport;
        _serializer = serializer;
        _localIdentity = localIdentity;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _repositoryIdFactory = repositoryIdFactory ?? (() => Guid.NewGuid().ToString("N"));
        _log = log ?? Logs.Print;
    }

    public async Task<SyncRepositoryInitializationResult> EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        _log("Sync repository ensure directories started.");
        await _transport.EnsureRepositoryAsync(cancellationToken).ConfigureAwait(false);
        _log("Sync repository ensure directories finished.");
        if (_transport is IWebDavTempObjectCleaner tempObjectCleaner)
        {
            _log("Sync repository temp cleanup started.");
            await tempObjectCleaner.CleanupStaleTempObjectsAsync(cancellationToken).ConfigureAwait(false);
            _log("Sync repository temp cleanup finished.");
        }

        _log("Sync repository manifest read started.");
        var existingManifestBytes = await _transport
            .GetAsync(SyncRemoteRepositoryLayout.ManifestPath, cancellationToken)
            .ConfigureAwait(false);

        if (existingManifestBytes is null)
        {
            _log("Sync repository manifest missing; creating new manifest.");
            var createdManifest = CreateManifest();
            var putResult = await _transport.PutAsync(
                SyncRemoteRepositoryLayout.ManifestPath,
                _serializer.Serialize(createdManifest),
                expectedEtag: null,
                createOnly: true,
                cancellationToken).ConfigureAwait(false);

            if (putResult.Status == SyncPutStatus.CreatedOrUpdated)
            {
                _log("Sync repository manifest created.");
                return SyncRepositoryInitializationResult.Ready(createdManifest);
            }

            _log("Sync repository manifest create conflicted; rereading manifest.");
            existingManifestBytes = await _transport
                .GetAsync(SyncRemoteRepositoryLayout.ManifestPath, cancellationToken)
                .ConfigureAwait(false);

            if (existingManifestBytes is null)
            {
                _log("Sync repository initialization failed: manifest is still missing.");
                return SyncRepositoryInitializationResult.InvalidRepository();
            }
        }
        else
        {
            _log($"Sync repository manifest read finished. Bytes={existingManifestBytes.Length}.");
        }

        SyncManifestDto manifest;
        try
        {
            manifest = _serializer.Deserialize<SyncManifestDto>(existingManifestBytes);
        }
        catch (JsonException)
        {
            _log("Sync repository initialization failed: manifest JSON is invalid.");
            return SyncRepositoryInitializationResult.InvalidRepository();
        }

        if (manifest.MinimumAppSyncVersion > SyncRemoteObjectConstants.FormatVersion ||
            manifest.FormatVersion > SyncRemoteObjectConstants.FormatVersion)
        {
            _log("Sync repository initialization failed: unsupported manifest version.");
            return SyncRepositoryInitializationResult.UnsupportedRepositoryVersion(manifest);
        }

        try
        {
            SyncRemoteObjectValidator.Validate(manifest);
        }
        catch (SyncRemoteObjectValidationException)
        {
            _log("Sync repository initialization failed: manifest validation failed.");
            return SyncRepositoryInitializationResult.InvalidRepository();
        }

        _log("Sync repository manifest validated.");
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
