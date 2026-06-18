using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync;

namespace Stranichnik.Sync.WebDav;

public interface IWebDavSyncTransport
{
    Task EnsureRepositoryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SyncRemoteObjectInfo>> ListAsync(
        string relativeDirectory,
        CancellationToken cancellationToken);

    Task<byte[]?> GetAsync(
        string relativePath,
        CancellationToken cancellationToken);

    Task<SyncPutResult> PutAsync(
        string relativePath,
        byte[] bytes,
        string? expectedEtag,
        bool createOnly,
        CancellationToken cancellationToken);
}
