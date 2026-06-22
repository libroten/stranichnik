using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Stranichnik.Security;
using Stranichnik.Sync.Local;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteSecretProfileStore : ISecretProfileStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSecretProfileStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public CryptoProfileRecord? LoadActiveProfile()
    {
        using var connection = _connectionFactory.OpenConnection();
        return LoadActiveProfile(connection, transaction: null);
    }

    internal static CryptoProfileRecord? LoadActiveProfile(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = CreateSelectByIdCommand(connection, SecretCryptoProfileIds.ActiveProfileId);
        command.Transaction = transaction;
        using var reader = command.ExecuteReader();

        return reader.Read()
            ? ReadProfile(reader)
            : null;
    }

    public IReadOnlyList<SyncCryptoProfileSnapshotRecord> LoadAllProfilesForSync()
    {
        using var connection = _connectionFactory.OpenConnection();
        return LoadAllProfilesForSync(connection, transaction: null);
    }

    internal static IReadOnlyList<SyncCryptoProfileSnapshotRecord> LoadAllProfilesForSync(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                profile_version,
                kdf_name,
                kdf_hash_algorithm,
                kdf_iterations,
                kdf_salt,
                kek_length_bytes,
                data_key_algorithm,
                wrapped_data_key,
                wrapped_data_key_nonce,
                encryption_algorithm,
                payload_format,
                password_check_payload,
                password_check_nonce,
                created_at_utc,
                updated_at_utc,
                secret_generation_id,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                content_hash,
                modified_device_id
            FROM crypto_profiles
            ORDER BY updated_at_utc ASC, id ASC;
            """;

        var profiles = new List<SyncCryptoProfileSnapshotRecord>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            profiles.Add(new SyncCryptoProfileSnapshotRecord(
                ReadProfile(reader),
                ReadSyncObjectMetadata(reader)));
        }

        return profiles;
    }

    public CryptoProfileRecord SaveNewProfile(CryptoProfileRecord profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (ProfileExists(connection, transaction, profile.Id))
            throw new InvalidOperationException("Secret crypto profile already exists.");

        InsertProfile(connection, transaction, profile);
        transaction.Commit();
        return profile;
    }

    public CryptoProfileRecord UpdateProfile(CryptoProfileRecord profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE crypto_profiles
            SET
                profile_version = $profileVersion,
                kdf_name = $kdfName,
                kdf_hash_algorithm = $kdfHashAlgorithm,
                kdf_iterations = $kdfIterations,
                kdf_salt = $kdfSalt,
                kek_length_bytes = $kekLengthBytes,
                data_key_algorithm = $dataKeyAlgorithm,
                wrapped_data_key = $wrappedDataKey,
                wrapped_data_key_nonce = $wrappedDataKeyNonce,
                encryption_algorithm = $encryptionAlgorithm,
                payload_format = $payloadFormat,
                password_check_payload = $passwordCheckPayload,
                password_check_nonce = $passwordCheckNonce,
                created_at_utc = $createdAtUtc,
                updated_at_utc = $updatedAtUtc,
                secret_generation_id = $secretGenerationId,
                sync_state = $syncState
            WHERE id = $id;
            """;
        AddProfileParameters(command, profile);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(BookmarkSyncState.Dirty));

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret crypto profile does not exist.");

        transaction.Commit();
        return profile;
    }

    internal void UpsertRemoteProfile(
        CryptoProfileRecord profile,
        SyncObjectMetadata syncMetadata)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(syncMetadata);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        UpsertRemoteProfile(connection, transaction, profile, syncMetadata);

        transaction.Commit();
    }

    internal static void UpsertRemoteProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CryptoProfileRecord profile,
        SyncObjectMetadata syncMetadata)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(syncMetadata);

        UpsertRemoteProfileCore(connection, transaction, profile, syncMetadata);
    }

    internal void MarkSyncMetadata(
        string secretGenerationId,
        BookmarkSyncState syncState,
        string? remoteEtag,
        DateTimeOffset? lastSyncedAtUtc,
        string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var connection = _connectionFactory.OpenConnection();
        MarkSyncMetadata(connection, transaction: null, secretGenerationId, syncState, remoteEtag, lastSyncedAtUtc, contentHash);
    }

    internal static void MarkSyncMetadata(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string secretGenerationId,
        BookmarkSyncState syncState,
        string? remoteEtag,
        DateTimeOffset? lastSyncedAtUtc,
        string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE crypto_profiles
            SET
                sync_state = $syncState,
                remote_etag = $remoteEtag,
                last_synced_at_utc = $lastSyncedAtUtc,
                content_hash = $contentHash
            WHERE secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(syncState));
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(remoteEtag));
        command.Parameters.AddWithValue("$lastSyncedAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(lastSyncedAtUtc));
        command.Parameters.AddWithValue("$contentHash", SqliteBookmarkItemMapper.ToDatabaseValue(contentHash));

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret crypto profile was not found.");
    }

    internal void MarkSyncState(
        string secretGenerationId,
        BookmarkSyncState syncState)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var connection = _connectionFactory.OpenConnection();
        MarkSyncState(connection, transaction: null, secretGenerationId, syncState);
    }

    internal static void MarkSyncState(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string secretGenerationId,
        BookmarkSyncState syncState)
    {
        if (string.IsNullOrWhiteSpace(secretGenerationId))
            throw new ArgumentException("Secret generation ID cannot be empty.", nameof(secretGenerationId));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE crypto_profiles
            SET sync_state = $syncState
            WHERE secret_generation_id = $secretGenerationId;
            """;
        command.Parameters.AddWithValue("$secretGenerationId", secretGenerationId);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(syncState));

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret crypto profile was not found.");
    }

    private static SqliteCommand CreateSelectByIdCommand(SqliteConnection connection, long profileId)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                profile_version,
                kdf_name,
                kdf_hash_algorithm,
                kdf_iterations,
                kdf_salt,
                kek_length_bytes,
                data_key_algorithm,
                wrapped_data_key,
                wrapped_data_key_nonce,
                encryption_algorithm,
                payload_format,
                password_check_payload,
                password_check_nonce,
                created_at_utc,
                updated_at_utc,
                secret_generation_id
            FROM crypto_profiles
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", profileId);
        return command;
    }

    private static bool ProfileExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long profileId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM crypto_profiles WHERE id = $id;";
        command.Parameters.AddWithValue("$id", profileId);
        return command.ExecuteScalar() is not null;
    }

    private static void InsertProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CryptoProfileRecord profile)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO crypto_profiles (
                id,
                profile_version,
                kdf_name,
                kdf_hash_algorithm,
                kdf_iterations,
                kdf_salt,
                kek_length_bytes,
                data_key_algorithm,
                wrapped_data_key,
                wrapped_data_key_nonce,
                encryption_algorithm,
                payload_format,
                password_check_payload,
                password_check_nonce,
                created_at_utc,
                updated_at_utc,
                secret_generation_id)
            VALUES (
                $id,
                $profileVersion,
                $kdfName,
                $kdfHashAlgorithm,
                $kdfIterations,
                $kdfSalt,
                $kekLengthBytes,
                $dataKeyAlgorithm,
                $wrappedDataKey,
                $wrappedDataKeyNonce,
                $encryptionAlgorithm,
                $payloadFormat,
                $passwordCheckPayload,
                $passwordCheckNonce,
                $createdAtUtc,
                $updatedAtUtc,
                $secretGenerationId);
            """;
        AddProfileParameters(command, profile);
        command.ExecuteNonQuery();
    }

    private static void UpsertRemoteProfileCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CryptoProfileRecord profile,
        SyncObjectMetadata syncMetadata)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO crypto_profiles (
                id,
                profile_version,
                kdf_name,
                kdf_hash_algorithm,
                kdf_iterations,
                kdf_salt,
                kek_length_bytes,
                data_key_algorithm,
                wrapped_data_key,
                wrapped_data_key_nonce,
                encryption_algorithm,
                payload_format,
                password_check_payload,
                password_check_nonce,
                created_at_utc,
                updated_at_utc,
                secret_generation_id,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                content_hash,
                modified_device_id)
            VALUES (
                $id,
                $profileVersion,
                $kdfName,
                $kdfHashAlgorithm,
                $kdfIterations,
                $kdfSalt,
                $kekLengthBytes,
                $dataKeyAlgorithm,
                $wrappedDataKey,
                $wrappedDataKeyNonce,
                $encryptionAlgorithm,
                $payloadFormat,
                $passwordCheckPayload,
                $passwordCheckNonce,
                $createdAtUtc,
                $updatedAtUtc,
                $secretGenerationId,
                $syncState,
                $remoteEtag,
                $lastSyncedAtUtc,
                $contentHash,
                $modifiedDeviceId)
            ON CONFLICT(id) DO UPDATE SET
                profile_version = excluded.profile_version,
                kdf_name = excluded.kdf_name,
                kdf_hash_algorithm = excluded.kdf_hash_algorithm,
                kdf_iterations = excluded.kdf_iterations,
                kdf_salt = excluded.kdf_salt,
                kek_length_bytes = excluded.kek_length_bytes,
                data_key_algorithm = excluded.data_key_algorithm,
                wrapped_data_key = excluded.wrapped_data_key,
                wrapped_data_key_nonce = excluded.wrapped_data_key_nonce,
                encryption_algorithm = excluded.encryption_algorithm,
                payload_format = excluded.payload_format,
                password_check_payload = excluded.password_check_payload,
                password_check_nonce = excluded.password_check_nonce,
                created_at_utc = excluded.created_at_utc,
                updated_at_utc = excluded.updated_at_utc,
                secret_generation_id = excluded.secret_generation_id,
                sync_state = excluded.sync_state,
                remote_etag = excluded.remote_etag,
                last_synced_at_utc = excluded.last_synced_at_utc,
                content_hash = excluded.content_hash,
                modified_device_id = excluded.modified_device_id;
            """;
        AddProfileParameters(command, profile);
        AddSyncMetadataParameters(command, syncMetadata);
        command.ExecuteNonQuery();
    }

    private static void AddProfileParameters(SqliteCommand command, CryptoProfileRecord profile)
    {
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$profileVersion", profile.ProfileVersion);
        command.Parameters.AddWithValue("$kdfName", profile.KdfName);
        command.Parameters.AddWithValue("$kdfHashAlgorithm", profile.KdfHashAlgorithm);
        command.Parameters.AddWithValue("$kdfIterations", profile.KdfIterations);
        command.Parameters.AddWithValue("$kdfSalt", profile.KdfSalt.ToArray());
        command.Parameters.AddWithValue("$kekLengthBytes", profile.KekLengthBytes);
        command.Parameters.AddWithValue("$dataKeyAlgorithm", profile.DataKeyAlgorithm);
        command.Parameters.AddWithValue("$wrappedDataKey", profile.WrappedDataKey.ToArray());
        command.Parameters.AddWithValue("$wrappedDataKeyNonce", profile.WrappedDataKeyNonce.ToArray());
        command.Parameters.AddWithValue("$encryptionAlgorithm", profile.EncryptionAlgorithm);
        command.Parameters.AddWithValue("$payloadFormat", profile.PayloadFormat);
        command.Parameters.AddWithValue("$passwordCheckPayload", profile.PasswordCheckPayload.ToArray());
        command.Parameters.AddWithValue("$passwordCheckNonce", profile.PasswordCheckNonce.ToArray());
        command.Parameters.AddWithValue("$createdAtUtc", SqliteBookmarkItemMapper.FormatDateTime(profile.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", SqliteBookmarkItemMapper.FormatDateTime(profile.UpdatedAtUtc));
        command.Parameters.AddWithValue("$secretGenerationId", profile.SecretGenerationId);
    }

    private static void AddSyncMetadataParameters(
        SqliteCommand command,
        SyncObjectMetadata syncMetadata)
    {
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(syncMetadata.SyncState));
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(syncMetadata.RemoteEtag));
        command.Parameters.AddWithValue("$lastSyncedAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(syncMetadata.LastSyncedAtUtc));
        command.Parameters.AddWithValue("$contentHash", SqliteBookmarkItemMapper.ToDatabaseValue(syncMetadata.ContentHash));
        command.Parameters.AddWithValue("$modifiedDeviceId", syncMetadata.ModifiedDeviceId);
    }

    private static CryptoProfileRecord ReadProfile(SqliteDataReader reader)
    {
        return new CryptoProfileRecord(
            reader.GetInt64(reader.GetOrdinal("id")),
            reader.GetInt32(reader.GetOrdinal("profile_version")),
            reader.GetString(reader.GetOrdinal("kdf_name")),
            reader.GetString(reader.GetOrdinal("kdf_hash_algorithm")),
            reader.GetInt32(reader.GetOrdinal("kdf_iterations")),
            ReadRequiredBytes(reader, "kdf_salt"),
            reader.GetInt32(reader.GetOrdinal("kek_length_bytes")),
            reader.GetString(reader.GetOrdinal("data_key_algorithm")),
            ReadRequiredBytes(reader, "wrapped_data_key"),
            ReadRequiredBytes(reader, "wrapped_data_key_nonce"),
            reader.GetString(reader.GetOrdinal("encryption_algorithm")),
            reader.GetString(reader.GetOrdinal("payload_format")),
            ReadRequiredBytes(reader, "password_check_payload"),
            ReadRequiredBytes(reader, "password_check_nonce"),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("created_at_utc"))),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("updated_at_utc"))),
            reader.GetString(reader.GetOrdinal("secret_generation_id")));
    }

    private static SyncObjectMetadata ReadSyncObjectMetadata(SqliteDataReader reader)
    {
        return new SyncObjectMetadata(
            SqliteBookmarkItemMapper.ToBookmarkSyncState(reader.GetString(reader.GetOrdinal("sync_state"))),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "remote_etag"),
            SqliteBookmarkItemMapper.ReadNullableDateTime(reader, "last_synced_at_utc"),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "content_hash"),
            reader.GetString(reader.GetOrdinal("modified_device_id")));
    }

    private static byte[] ReadRequiredBytes(SqliteDataReader reader, string name)
    {
        return SqliteBookmarkItemMapper.ReadNullableBytes(reader, name)
            ?? throw new InvalidOperationException("SQLite crypto profile payload is missing.");
    }
}
