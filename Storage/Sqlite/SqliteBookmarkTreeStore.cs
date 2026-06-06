using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteBookmarkTreeStore : IBookmarkTreeStore
{
    private const long SortOrderStep = 1000;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Func<string> _idFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _modifiedDeviceId;

    public SqliteBookmarkTreeStore(
        SqliteConnectionFactory connectionFactory,
        Func<string>? idFactory = null,
        Func<DateTimeOffset>? clock = null,
        string? modifiedDeviceId = null)
    {
        _connectionFactory = connectionFactory;
        _idFactory = idFactory ?? (() => Guid.NewGuid().ToString("N"));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _modifiedDeviceId = string.IsNullOrWhiteSpace(modifiedDeviceId)
            ? "local"
            : modifiedDeviceId;
    }

    public BookmarkTreeSnapshot Load()
    {
        using var connection = _connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                parent_id,
                item_type,
                sort_order,
                title,
                url,
                icon_asset_id,
                is_secret,
                encrypted_payload,
                encryption_nonce,
                crypto_profile_id,
                secret_payload_format_version,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id
            FROM items
            WHERE deleted_at_utc IS NULL
            ORDER BY COALESCE(parent_id, ''), sort_order DESC, id ASC;
            """;

        var items = new List<BookmarkItemRecord>();

        using var reader = command.ExecuteReader();
        while (reader.Read())
            items.Add(ReadRecord(reader));

        return new BookmarkTreeSnapshot(items);
    }

    public BookmarkIconAssetRecord? GetIconAsset(string iconAssetId)
    {
        ArgumentNullException.ThrowIfNull(iconAssetId);

        using var connection = _connectionFactory.OpenConnection();
        using var command = CreateIconAssetByIdCommand(connection, iconAssetId);
        using var reader = command.ExecuteReader();

        return reader.Read()
            ? ReadIconAssetRecord(reader)
            : null;
    }

    public BookmarkIconAssetRecord? GetIconAssetBySourceHash(
        string sourceHashAlgorithm,
        string sourceHash)
    {
        ArgumentNullException.ThrowIfNull(sourceHashAlgorithm);
        ArgumentNullException.ThrowIfNull(sourceHash);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var iconAsset = TryGetIconAssetBySourceHash(
            connection,
            transaction,
            sourceHashAlgorithm,
            sourceHash);
        transaction.Commit();
        return iconAsset;
    }

    public BookmarkIconAssetRecord GetOrCreateIconAsset(BookmarkIconAssetRecord iconAsset)
    {
        ArgumentNullException.ThrowIfNull(iconAsset);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = TryGetIconAssetBySourceHash(
            connection,
            transaction,
            iconAsset.SourceHashAlgorithm,
            iconAsset.SourceHash);

        if (existing is not null)
        {
            transaction.Commit();
            return existing;
        }

        InsertIconAsset(connection, transaction, iconAsset);
        transaction.Commit();

        return iconAsset;
    }

    public BookmarkItemRecord AddBookmarkToFolderStart(
        string? parentId,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var normalizedUrl = NormalizeRequired(url, nameof(url));

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        EnsureParentFolderExists(connection, transaction, parentId);

        var now = _clock();
        var record = new BookmarkItemRecord(
            _idFactory(),
            parentId,
            BookmarkItemKind.Bookmark,
            AllocateStartSortOrder(connection, transaction, parentId),
            normalizedTitle,
            normalizedUrl,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata(now));

        InsertRecord(connection, transaction, record);
        transaction.Commit();

        return record;
    }

    public BookmarkItemRecord AddSecretBookmarkToFolderStart(
        string? parentId,
        string bookmarkId,
        EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        var normalizedBookmarkId = NormalizeRequired(bookmarkId, nameof(bookmarkId));
        ValidateEncryptedPayload(encryptedPayload);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        EnsureParentFolderExists(connection, transaction, parentId);

        var now = _clock();
        var record = new BookmarkItemRecord(
            normalizedBookmarkId,
            parentId,
            BookmarkItemKind.Bookmark,
            AllocateStartSortOrder(connection, transaction, parentId),
            Title: null,
            Url: null,
            IsSecret: true,
            encryptedPayload,
            CreateMetadata(now));

        InsertRecord(connection, transaction, record);
        transaction.Commit();

        return record;
    }

    public BookmarkItemRecord AddFolderToFolderStart(
        string? parentId,
        string title)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        EnsureParentFolderExists(connection, transaction, parentId);

        var now = _clock();
        var record = new BookmarkItemRecord(
            _idFactory(),
            parentId,
            BookmarkItemKind.Folder,
            AllocateStartSortOrder(connection, transaction, parentId),
            normalizedTitle,
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata(now));

        InsertRecord(connection, transaction, record);
        transaction.Commit();

        return record;
    }

    public BookmarkItemRecord EditBookmark(
        string bookmarkId,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var normalizedUrl = NormalizeRequired(url, nameof(url));

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var bookmark = GetVisibleItem(connection, transaction, bookmarkId);

        if (bookmark.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only bookmarks can be edited as bookmarks.");

        if (bookmark.IsSecret)
            throw new InvalidOperationException("Secret bookmark editing is not implemented yet.");

        var updated = bookmark with
        {
            Title = normalizedTitle,
            Url = normalizedUrl,
            Metadata = Touch(bookmark.Metadata)
        };

        UpdateRecord(connection, transaction, updated);
        transaction.Commit();

        return updated;
    }

    public BookmarkItemRecord EditBookmarkAsSecret(
        string bookmarkId,
        EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        ValidateEncryptedPayload(encryptedPayload);

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var bookmark = GetVisibleItem(connection, transaction, bookmarkId);

        if (bookmark.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only bookmarks can be edited as secret bookmarks.");

        var updated = bookmark with
        {
            Title = null,
            Url = null,
            IsSecret = true,
            EncryptedPayload = encryptedPayload,
            IconAssetId = null,
            Metadata = Touch(bookmark.Metadata)
        };

        UpdateRecord(connection, transaction, updated);
        transaction.Commit();

        return updated;
    }

    public BookmarkItemRecord EditSecretBookmarkAsPlaintext(
        string bookmarkId,
        string title,
        string url)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));
        var normalizedUrl = NormalizeRequired(url, nameof(url));

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var bookmark = GetVisibleItem(connection, transaction, bookmarkId);

        if (bookmark.Kind != BookmarkItemKind.Bookmark)
            throw new InvalidOperationException("Only bookmarks can be edited as bookmarks.");

        if (!bookmark.IsSecret)
            throw new InvalidOperationException("Only secret bookmarks can be converted to plaintext bookmarks.");

        var updated = bookmark with
        {
            Title = normalizedTitle,
            Url = normalizedUrl,
            IsSecret = false,
            EncryptedPayload = null,
            IconAssetId = null,
            Metadata = Touch(bookmark.Metadata)
        };

        UpdateRecord(connection, transaction, updated);
        transaction.Commit();

        return updated;
    }

    public BookmarkItemRecord SetItemIconAsset(
        string itemId,
        string? iconAssetId)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var item = GetVisibleItem(connection, transaction, itemId);

        if (item.IsSecret && iconAssetId is not null)
            throw new InvalidOperationException("Secret bookmarks cannot use custom icons.");

        if (iconAssetId is not null && !IconAssetExists(connection, transaction, iconAssetId))
            throw new InvalidOperationException("Icon asset was not found.");

        var updated = item with
        {
            IconAssetId = iconAssetId,
            Metadata = Touch(item.Metadata)
        };

        UpdateRecord(connection, transaction, updated);
        transaction.Commit();

        return updated;
    }

    public BookmarkItemRecord EditFolder(
        string folderId,
        string title)
    {
        var normalizedTitle = NormalizeRequired(title, nameof(title));

        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var folder = GetVisibleItem(connection, transaction, folderId);

        if (folder.Kind != BookmarkItemKind.Folder)
            throw new InvalidOperationException("Only folders can be edited as folders.");

        var updated = folder with
        {
            Title = normalizedTitle,
            Metadata = Touch(folder.Metadata)
        };

        UpdateRecord(connection, transaction, updated);
        transaction.Commit();

        return updated;
    }

    public void DeleteItem(string itemId)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var item = GetVisibleItem(connection, transaction, itemId);
        var now = _clock();

        if (item.Kind == BookmarkItemKind.Folder)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                WITH RECURSIVE descendants(id) AS (
                    SELECT id FROM items WHERE id = $itemId AND deleted_at_utc IS NULL
                    UNION ALL
                    SELECT child.id
                    FROM items child
                    INNER JOIN descendants parent ON child.parent_id = parent.id
                    WHERE child.deleted_at_utc IS NULL
                )
                UPDATE items
                SET
                    deleted_at_utc = $deletedAtUtc,
                    updated_at_utc = $updatedAtUtc,
                    revision = revision + 1,
                    sync_state = $syncState,
                    modified_device_id = $modifiedDeviceId
                WHERE id IN (SELECT id FROM descendants)
                    AND deleted_at_utc IS NULL;
                """;
            AddCommonUpdateParameters(command, now);
            command.Parameters.AddWithValue("$itemId", itemId);
            command.ExecuteNonQuery();
        }
        else
        {
            var updated = item with
            {
                Metadata = Touch(item.Metadata, now) with
                {
                    DeletedAtUtc = now
                }
            };

            UpdateRecord(connection, transaction, updated);
        }

        transaction.Commit();
    }

    public bool CanMoveToFolderStart(
        string itemId,
        string? targetParentId)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var result = CanMoveToFolderStart(connection, transaction, itemId, targetParentId);
        transaction.Commit();
        return result;
    }

    public BookmarkItemRecord MoveToFolderStart(
        string itemId,
        string? targetParentId)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var item = GetVisibleItem(connection, transaction, itemId);

        if (!CanMoveToFolderStart(connection, transaction, itemId, targetParentId))
            throw new InvalidOperationException("Bookmark tree item cannot be moved to the target folder.");

        var updated = item with
        {
            ParentId = targetParentId,
            SortOrder = AllocateStartSortOrder(connection, transaction, targetParentId),
            Metadata = Touch(item.Metadata)
        };

        UpdateRecord(connection, transaction, updated);
        transaction.Commit();

        return updated;
    }

    internal void InsertSeedItems(BookmarkTreeSnapshot snapshot)
    {
        using var connection = _connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var item in snapshot.Items)
            InsertRecord(connection, transaction, item);

        transaction.Commit();
    }

    internal long CountAllItems()
    {
        using var connection = _connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool CanMoveToFolderStart(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string itemId,
        string? targetParentId)
    {
        if (!TryGetVisibleItem(connection, transaction, itemId, out var item))
            return false;

        if (!TryGetParentFolder(connection, transaction, targetParentId, out _))
            return false;

        if (item.ParentId == targetParentId)
            return false;

        if (item.Id == targetParentId)
            return false;

        if (item.Kind == BookmarkItemKind.Folder && IsDescendantOf(connection, transaction, targetParentId, item.Id))
            return false;

        return true;
    }

    private static BookmarkItemRecord ReadRecord(SqliteDataReader reader)
    {
        var encryptedPayload = SqliteBookmarkItemMapper.ReadNullableBytes(reader, "encrypted_payload");
        var encryptionNonce = SqliteBookmarkItemMapper.ReadNullableBytes(reader, "encryption_nonce");
        var cryptoProfileId = ReadNullableInt64(reader, "crypto_profile_id");
        var payloadFormatVersion = ReadNullableInt32(reader, "secret_payload_format_version");

        EncryptedBookmarkPayloadRecord? payload = null;

        if (encryptedPayload is not null &&
            encryptionNonce is not null &&
            cryptoProfileId is not null &&
            payloadFormatVersion is not null)
        {
            payload = new EncryptedBookmarkPayloadRecord(
                encryptedPayload,
                encryptionNonce,
                cryptoProfileId.Value,
                payloadFormatVersion.Value);
        }

        return new BookmarkItemRecord(
            reader.GetString(reader.GetOrdinal("id")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "parent_id"),
            SqliteBookmarkItemMapper.ToBookmarkItemKind(reader.GetString(reader.GetOrdinal("item_type"))),
            reader.GetInt64(reader.GetOrdinal("sort_order")),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "title"),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "url"),
            reader.GetInt64(reader.GetOrdinal("is_secret")) == 1,
            payload,
            new BookmarkItemMetadata(
                SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("created_at_utc"))),
                SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("updated_at_utc"))),
                SqliteBookmarkItemMapper.ReadNullableDateTime(reader, "deleted_at_utc"),
                reader.GetInt32(reader.GetOrdinal("revision")),
                SqliteBookmarkItemMapper.ToBookmarkSyncState(reader.GetString(reader.GetOrdinal("sync_state"))),
                SqliteBookmarkItemMapper.ReadNullableString(reader, "remote_etag"),
                SqliteBookmarkItemMapper.ReadNullableDateTime(reader, "last_synced_at_utc"),
                reader.GetString(reader.GetOrdinal("modified_device_id"))),
            SqliteBookmarkItemMapper.ReadNullableString(reader, "icon_asset_id"));
    }

    private static BookmarkIconAssetRecord ReadIconAssetRecord(SqliteDataReader reader)
    {
        return new(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("source_hash_algorithm")),
            reader.GetString(reader.GetOrdinal("source_hash")),
            reader.GetInt64(reader.GetOrdinal("source_size_bytes")),
            reader.GetString(reader.GetOrdinal("processed_mime_type")),
            reader.GetInt32(reader.GetOrdinal("processed_width")),
            reader.GetInt32(reader.GetOrdinal("processed_height")),
            SqliteBookmarkItemMapper.ReadNullableBytes(reader, "processed_bytes")
                ?? throw new InvalidOperationException("SQLite icon asset payload is missing."),
            SqliteBookmarkItemMapper.ParseDateTime(reader.GetString(reader.GetOrdinal("created_at_utc"))));
    }

    private static void InsertRecord(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookmarkItemRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO items (
                id,
                parent_id,
                item_type,
                sort_order,
                title,
                url,
                icon_asset_id,
                is_secret,
                encrypted_payload,
                encryption_nonce,
                crypto_profile_id,
                secret_payload_format_version,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id)
            VALUES (
                $id,
                $parentId,
                $itemType,
                $sortOrder,
                $title,
                $url,
                $iconAssetId,
                $isSecret,
                $encryptedPayload,
                $encryptionNonce,
                $cryptoProfileId,
                $secretPayloadFormatVersion,
                $createdAtUtc,
                $updatedAtUtc,
                $deletedAtUtc,
                $revision,
                $syncState,
                $remoteEtag,
                $lastSyncedAtUtc,
                $modifiedDeviceId);
            """;
        AddRecordParameters(command, record);
        command.ExecuteNonQuery();
    }

    private static void UpdateRecord(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookmarkItemRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE items
            SET
                parent_id = $parentId,
                item_type = $itemType,
                sort_order = $sortOrder,
                title = $title,
                url = $url,
                icon_asset_id = $iconAssetId,
                is_secret = $isSecret,
                encrypted_payload = $encryptedPayload,
                encryption_nonce = $encryptionNonce,
                crypto_profile_id = $cryptoProfileId,
                secret_payload_format_version = $secretPayloadFormatVersion,
                created_at_utc = $createdAtUtc,
                updated_at_utc = $updatedAtUtc,
                deleted_at_utc = $deletedAtUtc,
                revision = $revision,
                sync_state = $syncState,
                remote_etag = $remoteEtag,
                last_synced_at_utc = $lastSyncedAtUtc,
                modified_device_id = $modifiedDeviceId
            WHERE id = $id;
            """;
        AddRecordParameters(command, record);

        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Bookmark tree item was not found.");
    }

    private static void AddRecordParameters(SqliteCommand command, BookmarkItemRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$parentId", SqliteBookmarkItemMapper.ToDatabaseValue(record.ParentId));
        command.Parameters.AddWithValue("$itemType", SqliteBookmarkItemMapper.ToDatabaseValue(record.Kind));
        command.Parameters.AddWithValue("$sortOrder", record.SortOrder);
        command.Parameters.AddWithValue("$title", SqliteBookmarkItemMapper.ToDatabaseValue(record.Title));
        command.Parameters.AddWithValue("$url", SqliteBookmarkItemMapper.ToDatabaseValue(record.Url));
        command.Parameters.AddWithValue("$iconAssetId", SqliteBookmarkItemMapper.ToDatabaseValue(record.IconAssetId));
        command.Parameters.AddWithValue("$isSecret", record.IsSecret ? 1 : 0);
        command.Parameters.AddWithValue("$encryptedPayload", SqliteBookmarkItemMapper.ToDatabaseValue(record.EncryptedPayload?.Payload.ToArray()));
        command.Parameters.AddWithValue("$encryptionNonce", SqliteBookmarkItemMapper.ToDatabaseValue(record.EncryptedPayload?.Nonce.ToArray()));
        command.Parameters.AddWithValue("$cryptoProfileId", SqliteBookmarkItemMapper.ToDatabaseValue(record.EncryptedPayload?.CryptoProfileId));
        command.Parameters.AddWithValue("$secretPayloadFormatVersion", SqliteBookmarkItemMapper.ToDatabaseValue(record.EncryptedPayload?.PayloadFormatVersion));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteBookmarkItemMapper.FormatDateTime(record.Metadata.CreatedAtUtc));
        command.Parameters.AddWithValue("$updatedAtUtc", SqliteBookmarkItemMapper.FormatDateTime(record.Metadata.UpdatedAtUtc));
        command.Parameters.AddWithValue("$deletedAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(record.Metadata.DeletedAtUtc));
        command.Parameters.AddWithValue("$revision", record.Metadata.Revision);
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(record.Metadata.SyncState));
        command.Parameters.AddWithValue("$remoteEtag", SqliteBookmarkItemMapper.ToDatabaseValue(record.Metadata.RemoteEtag));
        command.Parameters.AddWithValue("$lastSyncedAtUtc", SqliteBookmarkItemMapper.ToDatabaseValue(record.Metadata.LastSyncedAtUtc));
        command.Parameters.AddWithValue("$modifiedDeviceId", record.Metadata.ModifiedDeviceId);
    }

    private void AddCommonUpdateParameters(SqliteCommand command, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("$deletedAtUtc", SqliteBookmarkItemMapper.FormatDateTime(now));
        command.Parameters.AddWithValue("$updatedAtUtc", SqliteBookmarkItemMapper.FormatDateTime(now));
        command.Parameters.AddWithValue("$syncState", SqliteBookmarkItemMapper.ToDatabaseValue(BookmarkSyncState.Dirty));
        command.Parameters.AddWithValue("$modifiedDeviceId", _modifiedDeviceId);
    }

    private static BookmarkItemRecord GetVisibleItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string itemId)
    {
        return TryGetVisibleItem(connection, transaction, itemId, out var item)
            ? item
            : throw new InvalidOperationException("Bookmark tree item was not found.");
    }

    private static bool TryGetVisibleItem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string itemId,
        out BookmarkItemRecord item)
    {
        using var command = CreateItemByIdCommand(connection, transaction, itemId);
        using var reader = command.ExecuteReader();

        if (reader.Read())
        {
            item = ReadRecord(reader);
            return true;
        }

        item = null!;
        return false;
    }

    private static void EnsureParentFolderExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? parentId)
    {
        if (!TryGetParentFolder(connection, transaction, parentId, out _))
            throw new InvalidOperationException("Parent item must be a folder.");
    }

    private static bool TryGetParentFolder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? parentId,
        out BookmarkItemRecord? parent)
    {
        parent = null;

        if (parentId is null)
            return true;

        if (!TryGetVisibleItem(connection, transaction, parentId, out var item) || item.Kind != BookmarkItemKind.Folder)
            return false;

        parent = item;
        return true;
    }

    private static SqliteCommand CreateItemByIdCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string itemId)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                parent_id,
                item_type,
                sort_order,
                title,
                url,
                icon_asset_id,
                is_secret,
                encrypted_payload,
                encryption_nonce,
                crypto_profile_id,
                secret_payload_format_version,
                created_at_utc,
                updated_at_utc,
                deleted_at_utc,
                revision,
                sync_state,
                remote_etag,
                last_synced_at_utc,
                modified_device_id
            FROM items
            WHERE id = $itemId
                AND deleted_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$itemId", itemId);
        return command;
    }

    private static SqliteCommand CreateIconAssetByIdCommand(SqliteConnection connection, string iconAssetId)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                id,
                source_hash_algorithm,
                source_hash,
                source_size_bytes,
                processed_mime_type,
                processed_width,
                processed_height,
                processed_bytes,
                created_at_utc
            FROM icon_assets
            WHERE id = $iconAssetId;
            """;
        command.Parameters.AddWithValue("$iconAssetId", iconAssetId);
        return command;
    }

    private static BookmarkIconAssetRecord? TryGetIconAssetBySourceHash(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceHashAlgorithm,
        string sourceHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                source_hash_algorithm,
                source_hash,
                source_size_bytes,
                processed_mime_type,
                processed_width,
                processed_height,
                processed_bytes,
                created_at_utc
            FROM icon_assets
            WHERE source_hash_algorithm = $sourceHashAlgorithm
                AND source_hash = $sourceHash;
            """;
        command.Parameters.AddWithValue("$sourceHashAlgorithm", sourceHashAlgorithm);
        command.Parameters.AddWithValue("$sourceHash", sourceHash);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? ReadIconAssetRecord(reader)
            : null;
    }

    private static bool IconAssetExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string iconAssetId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM icon_assets WHERE id = $iconAssetId;";
        command.Parameters.AddWithValue("$iconAssetId", iconAssetId);

        return command.ExecuteScalar() is not null;
    }

    private static void InsertIconAsset(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BookmarkIconAssetRecord iconAsset)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO icon_assets (
                id,
                source_hash_algorithm,
                source_hash,
                source_size_bytes,
                processed_mime_type,
                processed_width,
                processed_height,
                processed_bytes,
                created_at_utc)
            VALUES (
                $id,
                $sourceHashAlgorithm,
                $sourceHash,
                $sourceSizeBytes,
                $processedMimeType,
                $processedWidth,
                $processedHeight,
                $processedBytes,
                $createdAtUtc);
            """;
        command.Parameters.AddWithValue("$id", iconAsset.Id);
        command.Parameters.AddWithValue("$sourceHashAlgorithm", iconAsset.SourceHashAlgorithm);
        command.Parameters.AddWithValue("$sourceHash", iconAsset.SourceHash);
        command.Parameters.AddWithValue("$sourceSizeBytes", iconAsset.SourceSizeBytes);
        command.Parameters.AddWithValue("$processedMimeType", iconAsset.ProcessedMimeType);
        command.Parameters.AddWithValue("$processedWidth", iconAsset.ProcessedWidth);
        command.Parameters.AddWithValue("$processedHeight", iconAsset.ProcessedHeight);
        command.Parameters.AddWithValue("$processedBytes", iconAsset.ProcessedBytes.ToArray());
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            SqliteBookmarkItemMapper.FormatDateTime(iconAsset.CreatedAtUtc));
        command.ExecuteNonQuery();
    }

    private static long AllocateStartSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? parentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (parentId is null)
        {
            command.CommandText = """
                SELECT MAX(sort_order)
                FROM items
                WHERE parent_id IS NULL
                    AND deleted_at_utc IS NULL;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT MAX(sort_order)
                FROM items
                WHERE parent_id = $parentId
                    AND deleted_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("$parentId", parentId);
        }

        var value = command.ExecuteScalar();
        return (value is null || value == DBNull.Value ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)) + SortOrderStep;
    }

    private static bool IsDescendantOf(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? possibleDescendantId,
        string folderId)
    {
        if (possibleDescendantId is null)
            return false;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE ancestors(id, parent_id) AS (
                SELECT id, parent_id
                FROM items
                WHERE id = $possibleDescendantId
                    AND deleted_at_utc IS NULL

                UNION ALL

                SELECT parent.id, parent.parent_id
                FROM items parent
                INNER JOIN ancestors child ON child.parent_id = parent.id
                WHERE parent.deleted_at_utc IS NULL
            )
            SELECT 1
            FROM ancestors
            WHERE id = $folderId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$possibleDescendantId", possibleDescendantId);
        command.Parameters.AddWithValue("$folderId", folderId);

        return command.ExecuteScalar() is not null;
    }

    private BookmarkItemMetadata CreateMetadata(DateTimeOffset now)
    {
        return new(
            now,
            now,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Dirty,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            _modifiedDeviceId);
    }

    private BookmarkItemMetadata Touch(BookmarkItemMetadata metadata)
    {
        return Touch(metadata, _clock());
    }

    private BookmarkItemMetadata Touch(BookmarkItemMetadata metadata, DateTimeOffset now)
    {
        return metadata with
        {
            UpdatedAtUtc = now,
            Revision = metadata.Revision + 1,
            SyncState = BookmarkSyncState.Dirty,
            ModifiedDeviceId = _modifiedDeviceId
        };
    }

    private static long? ReadNullableInt64(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static int? ReadNullableInt32(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        var normalized = value.Trim();

        if (normalized.Length == 0)
            throw new ArgumentException("Value cannot be empty.", parameterName);

        return normalized;
    }

    private static void ValidateEncryptedPayload(EncryptedBookmarkPayloadRecord encryptedPayload)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);

        if (encryptedPayload.Payload.IsEmpty ||
            encryptedPayload.Nonce.IsEmpty ||
            encryptedPayload.CryptoProfileId <= 0 ||
            encryptedPayload.PayloadFormatVersion <= 0)
        {
            throw new ArgumentException("Encrypted bookmark payload is invalid.", nameof(encryptedPayload));
        }
    }
}
