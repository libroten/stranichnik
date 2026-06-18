using System;
using System.Text;
using Stranichnik.Sync.Remote;
using Stranichnik.Sync.Serialization;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SyncSerializationTests
{
    [Fact]
    public void Serializer_round_trips_remote_item()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var item = CreateItem("Example");

        var utf8Json = serializer.Serialize(item);
        var deserialized = serializer.Deserialize<SyncItemDto>(utf8Json);

        Assert.Equal(item, deserialized);
        SyncRemoteObjectValidator.Validate(deserialized);
    }

    [Fact]
    public void Serializer_uses_utf8_json_with_remote_property_names()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var item = CreateItem("Example");

        var utf8Json = serializer.Serialize(item);
        var json = Encoding.UTF8.GetString(utf8Json);

        Assert.Contains("\"formatVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"modifiedDeviceId\":\"device\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentHasher_returns_sha256_prefixed_lowercase_hash()
    {
        var hasher = new Sha256SyncContentHasher();

        var hash = hasher.ComputeHash("hello"u8);

        Assert.Equal("sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
    }

    [Fact]
    public void ContentHasher_returns_same_hash_for_same_serialized_object()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var hasher = new Sha256SyncContentHasher();
        var item = CreateItem("Example");

        var firstHash = hasher.ComputeHash(serializer.Serialize(item));
        var secondHash = hasher.ComputeHash(serializer.Serialize(item));

        Assert.Equal(firstHash, secondHash);
    }

    [Fact]
    public void ContentHasher_changes_when_user_content_changes()
    {
        var serializer = new SystemTextSyncJsonSerializer();
        var hasher = new Sha256SyncContentHasher();
        var original = CreateItem("Example");
        var changed = original with
        {
            Title = "Changed"
        };

        var originalHash = hasher.ComputeHash(serializer.Serialize(original));
        var changedHash = hasher.ComputeHash(serializer.Serialize(changed));

        Assert.NotEqual(originalHash, changedHash);
    }

    private static SyncItemDto CreateItem(string title)
    {
        return new(
            Schema: SyncRemoteObjectConstants.ItemSchema,
            FormatVersion: SyncRemoteObjectConstants.FormatVersion,
            Id: "bookmark",
            ParentId: null,
            Kind: SyncRemoteObjectConstants.BookmarkKind,
            SortOrder: 1000,
            Title: title,
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

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
