using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync;

namespace Stranichnik.Sync.WebDav;

public class InMemoryWebDavSyncTransport : IWebDavSyncTransport
{
    private readonly Dictionary<string, RemoteFile> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private long _etagSequence;

    public Task EnsureRepositoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var directory in SyncRemoteRepositoryLayout.RequiredDirectories)
            _directories.Add(directory);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncRemoteObjectInfo>> ListAsync(
        string relativeDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedDirectory = NormalizeDirectory(relativeDirectory);
        var prefix = string.IsNullOrEmpty(normalizedDirectory)
            ? string.Empty
            : normalizedDirectory + "/";

        var objects = _files
            .Where(file => file.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(file => !file.Key[prefix.Length..].Contains('/', StringComparison.Ordinal))
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new SyncRemoteObjectInfo(
                file.Key,
                file.Value.ETag,
                file.Value.LastModifiedUtc,
                file.Value.Bytes.Length))
            .ToList();

        return Task.FromResult<IReadOnlyList<SyncRemoteObjectInfo>>(objects);
    }

    public virtual Task<byte[]?> GetAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedPath = NormalizePath(relativePath);
        var bytes = _files.TryGetValue(normalizedPath, out var file)
            ? file.Bytes.ToArray()
            : null;

        return Task.FromResult(bytes);
    }

    public virtual Task<SyncPutResult> PutAsync(
        string relativePath,
        byte[] bytes,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedPath = NormalizePath(relativePath);
        var exists = _files.TryGetValue(normalizedPath, out var existing);

        if (createOnly && exists)
            return Task.FromResult(SyncPutResult.PreconditionFailed(existing?.ETag));

        if (expectedEtag is not null && (!exists || existing?.ETag != expectedEtag))
            return Task.FromResult(SyncPutResult.PreconditionFailed(existing?.ETag));

        EnsureParentDirectory(normalizedPath);
        var etag = CreateEtag();
        _files[normalizedPath] = new RemoteFile(
            bytes.ToArray(),
            etag,
            DateTimeOffset.UtcNow);

        return Task.FromResult(SyncPutResult.CreatedOrUpdated(etag));
    }

    public virtual Task<SyncDeleteResult> DeleteAsync(
        string relativePath,
        string? expectedEtag,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedPath = NormalizePath(relativePath);
        if (!_files.TryGetValue(normalizedPath, out var existing))
            return Task.FromResult(SyncDeleteResult.DeletedOrMissing());

        if (expectedEtag is not null && existing.ETag != expectedEtag)
            return Task.FromResult(SyncDeleteResult.PreconditionFailed(existing.ETag));

        _files.Remove(normalizedPath);
        return Task.FromResult(SyncDeleteResult.DeletedOrMissing());
    }

    public bool DirectoryExists(string relativeDirectory)
    {
        return _directories.Contains(NormalizeDirectory(relativeDirectory));
    }

    private void EnsureParentDirectory(string relativePath)
    {
        var separatorIndex = relativePath.LastIndexOf('/');
        if (separatorIndex <= 0)
            return;

        _directories.Add(relativePath[..separatorIndex]);
    }

    private string CreateEtag()
    {
        _etagSequence++;
        return $"\"memory-{_etagSequence}\"";
    }

    private static string NormalizeDirectory(string relativeDirectory)
    {
        return NormalizePath(relativeDirectory).TrimEnd('/');
    }

    private static string NormalizePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;

        return relativePath.Replace('\\', '/').Trim('/');
    }

    private sealed record RemoteFile(
        byte[] Bytes,
        string ETag,
        DateTimeOffset LastModifiedUtc);
}
