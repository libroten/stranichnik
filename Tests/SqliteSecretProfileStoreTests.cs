using System;
using System.IO;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.Storage.Sqlite;
using Stranichnik.Sync.Local;
using Stranichnik.Sync.Push;
using Xunit;

namespace Stranichnik.Tests;

public sealed class SqliteSecretProfileStoreTests
{
    [Fact]
    public void LoadActiveProfile_returns_null_when_profile_is_missing()
    {
        using var database = TempSqliteDatabase.Create();

        var profile = database.Store.LoadActiveProfile();

        Assert.Null(profile);
    }

    [Fact]
    public void SaveNewProfile_inserts_and_loads_profile()
    {
        using var database = TempSqliteDatabase.Create();
        var profile = CreateProfile(updatedAtUtc: CreatedAt);

        var saved = database.Store.SaveNewProfile(profile);
        var loaded = database.Store.LoadActiveProfile();

        AssertProfileEqual(profile, saved);
        AssertProfileEqual(profile, Assert.IsType<CryptoProfileRecord>(loaded));
    }

    [Fact]
    public void SaveNewProfile_rejects_duplicate_profile()
    {
        using var database = TempSqliteDatabase.Create();
        database.Store.SaveNewProfile(CreateProfile(updatedAtUtc: CreatedAt));

        Assert.Throws<InvalidOperationException>(
            () => database.Store.SaveNewProfile(CreateProfile(updatedAtUtc: UpdatedAt)));
    }

    [Fact]
    public void UpdateProfile_updates_existing_profile()
    {
        using var database = TempSqliteDatabase.Create();
        var original = CreateProfile(updatedAtUtc: CreatedAt);
        var updated = original with
        {
            KdfSalt = new byte[] { 31, 32, 33 },
            WrappedDataKey = new byte[] { 34, 35, 36 },
            WrappedDataKeyNonce = new byte[] { 37, 38, 39 },
            PasswordCheckPayload = new byte[] { 40, 41, 42 },
            PasswordCheckNonce = new byte[] { 43, 44, 45 },
            UpdatedAtUtc = UpdatedAt
        };

        database.Store.SaveNewProfile(original);

        var saved = database.Store.UpdateProfile(updated);
        var loaded = database.Store.LoadActiveProfile();

        AssertProfileEqual(updated, saved);
        AssertProfileEqual(updated, Assert.IsType<CryptoProfileRecord>(loaded));
    }

    [Fact]
    public void UpdateProfile_marks_profile_dirty_for_sync()
    {
        using var database = TempSqliteDatabase.Create();
        var original = CreateProfile(updatedAtUtc: CreatedAt);
        var updated = original with
        {
            KdfSalt = new byte[] { 31, 32, 33 },
            WrappedDataKey = new byte[] { 34, 35, 36 },
            WrappedDataKeyNonce = new byte[] { 37, 38, 39 },
            PasswordCheckPayload = new byte[] { 40, 41, 42 },
            PasswordCheckNonce = new byte[] { 43, 44, 45 },
            UpdatedAtUtc = UpdatedAt
        };

        database.Store.SaveNewProfile(original);
        database.Store.MarkSyncMetadata(
            original.SecretGenerationId,
            BookmarkSyncState.Clean,
            "remote-etag",
            CreatedAt,
            "sha256:old-content");

        database.Store.UpdateProfile(updated);

        var storedProfile = Assert.Single(database.Store.LoadAllProfilesForSync());
        Assert.Equal(BookmarkSyncState.Dirty, storedProfile.SyncMetadata.SyncState);
        Assert.Equal("remote-etag", storedProfile.SyncMetadata.RemoteEtag);
        Assert.Equal(CreatedAt, storedProfile.SyncMetadata.LastSyncedAtUtc);
        Assert.Equal("sha256:old-content", storedProfile.SyncMetadata.ContentHash);

        var pushPlan = SyncPushPlanner.Plan(new SyncLocalSnapshot(
            new SyncLocalIdentity("database", "device"),
            Items: [],
            IconAssets: [],
            SecretIconAssets: [],
            CryptoProfiles: [storedProfile],
            SecretResetEvents: [],
            PendingAssetRefs: [],
            DeferredSecretItems: [],
            QuarantinedRemoteObjects: []));
        Assert.Equal(updated.SecretGenerationId, Assert.Single(pushPlan.CryptoProfiles).Profile.SecretGenerationId);
    }

    [Fact]
    public void UpdateProfile_rejects_missing_profile()
    {
        using var database = TempSqliteDatabase.Create();

        Assert.Throws<InvalidOperationException>(
            () => database.Store.UpdateProfile(CreateProfile(updatedAtUtc: UpdatedAt)));
    }

    private static CryptoProfileRecord CreateProfile(DateTimeOffset updatedAtUtc)
    {
        return new CryptoProfileRecord(
            SecretCryptoProfileIds.ActiveProfileId,
            SecretEncryptionConstants.CurrentProfileVersion,
            SecretEncryptionConstants.KdfName,
            SecretEncryptionConstants.KdfHashAlgorithm,
            1000,
            new byte[] { 1, 2, 3 },
            SecretEncryptionConstants.KekLengthBytes,
            SecretEncryptionConstants.DataKeyAlgorithm,
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 },
            SecretEncryptionConstants.EncryptionAlgorithm,
            SecretEncryptionConstants.PayloadFormat,
            new byte[] { 10, 11, 12 },
            new byte[] { 13, 14, 15 },
            CreatedAt,
            updatedAtUtc,
            "secret-generation");
    }

    private static void AssertProfileEqual(CryptoProfileRecord expected, CryptoProfileRecord actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.ProfileVersion, actual.ProfileVersion);
        Assert.Equal(expected.KdfName, actual.KdfName);
        Assert.Equal(expected.KdfHashAlgorithm, actual.KdfHashAlgorithm);
        Assert.Equal(expected.KdfIterations, actual.KdfIterations);
        Assert.Equal(expected.KdfSalt.ToArray(), actual.KdfSalt.ToArray());
        Assert.Equal(expected.KekLengthBytes, actual.KekLengthBytes);
        Assert.Equal(expected.DataKeyAlgorithm, actual.DataKeyAlgorithm);
        Assert.Equal(expected.WrappedDataKey.ToArray(), actual.WrappedDataKey.ToArray());
        Assert.Equal(expected.WrappedDataKeyNonce.ToArray(), actual.WrappedDataKeyNonce.ToArray());
        Assert.Equal(expected.EncryptionAlgorithm, actual.EncryptionAlgorithm);
        Assert.Equal(expected.PayloadFormat, actual.PayloadFormat);
        Assert.Equal(expected.PasswordCheckPayload.ToArray(), actual.PasswordCheckPayload.ToArray());
        Assert.Equal(expected.PasswordCheckNonce.ToArray(), actual.PasswordCheckNonce.ToArray());
        Assert.Equal(expected.CreatedAtUtc, actual.CreatedAtUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
        Assert.Equal(expected.SecretGenerationId, actual.SecretGenerationId);
    }

    private sealed class TempSqliteDatabase : IDisposable
    {
        private readonly string _directoryPath;

        private TempSqliteDatabase(string directoryPath)
        {
            _directoryPath = directoryPath;
            var connectionFactory = new SqliteConnectionFactory(Path.Combine(_directoryPath, "test.sqlite"));
            new SqliteDatabaseMigrator(connectionFactory).Migrate();
            Store = new SqliteSecretProfileStore(connectionFactory);
        }

        internal SqliteSecretProfileStore Store { get; }

        public static TempSqliteDatabase Create()
        {
            return new TempSqliteDatabase(Path.Combine(Path.GetTempPath(), $"stranichnik-tests-{Guid.NewGuid():N}"));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                    Directory.Delete(_directoryPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
}
