using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class InMemoryWebDavSyncTransportTests
{
    private static readonly byte[] FirstBytes = [1];
    private static readonly byte[] SecondBytes = [2];
    private static readonly byte[] ThirdBytes = [3];

    [Fact]
    public async Task EnsureRepositoryAsync_creates_required_directories()
    {
        var transport = new InMemoryWebDavSyncTransport();

        await transport.EnsureRepositoryAsync(CancellationToken.None);

        Assert.All(
            SyncRemoteRepositoryLayout.RequiredDirectories,
            directory => Assert.True(transport.DirectoryExists(directory)));
    }

    [Fact]
    public async Task PutGetAndListAsync_roundtrip_file()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var bytes = Encoding.UTF8.GetBytes("{}");

        var putResult = await transport.PutAsync(
            "items/item.json",
            bytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var loaded = await transport.GetAsync("items/item.json", CancellationToken.None);
        var listed = await transport.ListAsync("items", CancellationToken.None);

        Assert.Equal(SyncPutStatus.CreatedOrUpdated, putResult.Status);
        Assert.NotNull(putResult.ETag);
        Assert.Equal(bytes, loaded);
        var listedObject = Assert.Single(listed);
        Assert.Equal("items/item.json", listedObject.RelativePath);
        Assert.Equal(putResult.ETag, listedObject.ETag);
        Assert.Equal(bytes.Length, listedObject.ContentLength);
    }

    [Fact]
    public async Task PutAsync_create_only_fails_when_file_exists()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var first = await transport.PutAsync(
            "items/item.json",
            FirstBytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var second = await transport.PutAsync(
            "items/item.json",
            SecondBytes,
            expectedEtag: null,
            createOnly: true,
            cancellationToken: CancellationToken.None);

        var loaded = await transport.GetAsync("items/item.json", CancellationToken.None);

        Assert.Equal(SyncPutStatus.PreconditionFailed, second.Status);
        Assert.Equal(first.ETag, second.ETag);
        Assert.Equal(FirstBytes, loaded);
    }

    [Fact]
    public async Task PutAsync_expected_etag_fails_when_etag_changed()
    {
        var transport = new InMemoryWebDavSyncTransport();
        var first = await transport.PutAsync(
            "items/item.json",
            FirstBytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var second = await transport.PutAsync(
            "items/item.json",
            SecondBytes,
            expectedEtag: first.ETag,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var conflict = await transport.PutAsync(
            "items/item.json",
            ThirdBytes,
            expectedEtag: first.ETag,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var loaded = await transport.GetAsync("items/item.json", CancellationToken.None);

        Assert.Equal(SyncPutStatus.CreatedOrUpdated, second.Status);
        Assert.NotEqual(first.ETag, second.ETag);
        Assert.Equal(SyncPutStatus.PreconditionFailed, conflict.Status);
        Assert.Equal(second.ETag, conflict.ETag);
        Assert.Equal(SecondBytes, loaded);
    }

    [Fact]
    public async Task ListAsync_returns_only_direct_children()
    {
        var transport = new InMemoryWebDavSyncTransport();
        await transport.PutAsync(
            "items/first.json",
            FirstBytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);
        await transport.PutAsync(
            "items/nested/second.json",
            SecondBytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var listed = await transport.ListAsync("items", CancellationToken.None);

        var listedObject = Assert.Single(listed);
        Assert.Equal("items/first.json", listedObject.RelativePath);
    }

    [Fact]
    public async Task ListAsync_orders_objects_by_relative_path()
    {
        var transport = new InMemoryWebDavSyncTransport();
        await transport.PutAsync(
            "items/b.json",
            FirstBytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);
        await transport.PutAsync(
            "items/a.json",
            SecondBytes,
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);

        var listed = await transport.ListAsync("items", CancellationToken.None);

        Assert.Equal(
            ["items/a.json", "items/b.json"],
            listed.Select(item => item.RelativePath).ToArray());
    }
}
