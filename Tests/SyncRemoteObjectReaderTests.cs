using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Stranichnik.Sync;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Stranichnik.Sync.WebDav;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncRemoteObjectReaderTests
{
    [Fact]
    public async Task ReadObjectsAsync_downloads_deserializes_and_validates_objects()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new InMemoryWebDavSyncTransport();
        var item = SyncTestDtoHasher.WithContentHash(CreateItem("bookmark"), serializer);
        await PutJsonAsync(transport, serializer, SyncObjectKind.Item, item.Id, item);
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.Success, result.Status);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), result.Identity);
        Assert.Equal(item, result.Value);
    }

    [Fact]
    public async Task ReadObjectsAsync_reports_invalid_path()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new InMemoryWebDavSyncTransport();
        await transport.PutAsync(
            "items/not-json",
            serializer.Serialize(CreateItem("bookmark")),
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.InvalidPath, result.Status);
        Assert.Null(result.Identity);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadObjectsAsync_reports_missing_content()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new MissingContentTransport();
        await PutJsonAsync(transport, serializer, SyncObjectKind.Item, "bookmark", CreateItem("bookmark"));
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.MissingContent, result.Status);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), result.Identity);
    }

    [Fact]
    public async Task ReadObjectsAsync_reports_invalid_json()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new InMemoryWebDavSyncTransport();
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark")),
            Encoding.UTF8.GetBytes("{not-json"),
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.InvalidJson, result.Status);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), result.Identity);
    }

    [Fact]
    public async Task ReadObjectsAsync_reports_invalid_remote_object()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new InMemoryWebDavSyncTransport();
        var invalidItem = CreateItem("bookmark") with
        {
            Schema = "wrong"
        };
        await PutJsonAsync(transport, serializer, SyncObjectKind.Item, invalidItem.Id, invalidItem);
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.InvalidRemoteObject, result.Status);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), result.Identity);
    }

    [Fact]
    public async Task ReadObjectsAsync_reports_invalid_remote_object_for_content_hash_mismatch()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new InMemoryWebDavSyncTransport();
        var item = SyncTestDtoHasher.WithContentHash(CreateItem("bookmark"), serializer) with
        {
            Title = "Changed without updating hash"
        };
        await PutRawJsonAsync(transport, serializer, SyncObjectKind.Item, item.Id, item);
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.InvalidRemoteObject, result.Status);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), result.Identity);
    }

    [Fact]
    public async Task ReadObjectsAsync_reports_invalid_remote_object_for_invalid_base64()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var transport = new InMemoryWebDavSyncTransport();
        var item = SyncTestDtoHasher.WithContentHash(CreateItem("bookmark") with
        {
            IsSecret = true,
            Title = null,
            Url = null,
            EncryptedPayload = "not-base64",
            EncryptionNonce = Convert.ToBase64String([1, 2, 3]),
            CryptoProfileSecretGenerationId = "generation",
            SecretPayloadFormatVersion = 1
        }, serializer);
        await PutJsonAsync(transport, serializer, SyncObjectKind.Item, item.Id, item);
        var reader = new SyncRemoteObjectReader(transport, serializer);

        var results = await reader.ReadObjectsAsync<SyncItemDto>(SyncObjectKind.Item, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(SyncRemoteReadStatus.InvalidRemoteObject, result.Status);
        Assert.Equal(new SyncObjectIdentity(SyncObjectKind.Item, "bookmark"), result.Identity);
    }

    private static async Task PutJsonAsync<T>(
        InMemoryWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer,
        SyncObjectKind kind,
        string id,
        T value)
    {
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(kind, id)),
            serializer.Serialize(SyncTestDtoHasher.WithContentHash(value, serializer)),
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);
    }

    private static async Task PutRawJsonAsync<T>(
        InMemoryWebDavSyncTransport transport,
        SystemTextSyncJsonSerializer serializer,
        SyncObjectKind kind,
        string id,
        T value)
    {
        await transport.PutAsync(
            SyncRemoteObjectPath.ToRelativePath(new SyncObjectIdentity(kind, id)),
            serializer.Serialize(value),
            expectedEtag: null,
            createOnly: false,
            cancellationToken: CancellationToken.None);
    }

    private static SyncItemDto CreateItem(string id)
    {
        return new SyncItemDto(
            Schema: SyncRemoteObjectConstants.ItemSchema,
            FormatVersion: SyncRemoteObjectConstants.FormatVersion,
            Id: id,
            ParentId: null,
            Kind: SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: "Example",
            Url: "https://example.com/",
            IsSecret: false,
            IconAssetRef: null,
            SecretIconAssetRef: null,
            EncryptedPayload: null,
            EncryptionNonce: null,
            CryptoProfileSecretGenerationId: null,
            SecretPayloadFormatVersion: null,
            CreatedAtUtc: CreatedAt,
            UpdatedAtUtc: CreatedAt,
            DeletedAtUtc: null,
            Revision: 1,
            ModifiedDeviceId: "device",
            ContentHash: "sha256:content");
    }

    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    private sealed class MissingContentTransport : InMemoryWebDavSyncTransport
    {
        public override Task<byte[]?> GetAsync(
            string relativePath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }
}
