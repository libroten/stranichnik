using System;
using Microsoft.Data.Sqlite;
using Stranichnik.Security;

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
        using var command = CreateSelectByIdCommand(connection, SecretCryptoProfileIds.ActiveProfileId);
        using var reader = command.ExecuteReader();

        return reader.Read()
            ? ReadProfile(reader)
            : null;
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
                updated_at_utc = $updatedAtUtc
            WHERE id = $id;
            """;
        AddProfileParameters(command, profile);

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Secret crypto profile does not exist.");

        transaction.Commit();
        return profile;
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
                updated_at_utc
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
                updated_at_utc)
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
                $updatedAtUtc);
            """;
        AddProfileParameters(command, profile);
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
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("updated_at_utc"))));
    }

    private static byte[] ReadRequiredBytes(SqliteDataReader reader, string name)
    {
        return SqliteBookmarkItemMapper.ReadNullableBytes(reader, name)
            ?? throw new InvalidOperationException("SQLite crypto profile payload is missing.");
    }
}
