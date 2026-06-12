using System;
using System.Collections.Generic;
using System.Linq;
using Stranichnik.Icons;
using Stranichnik.Search;
using Stranichnik.Searching;
using Stranichnik.Security;
using Stranichnik.Storage;
using Stranichnik.ViewModels;
using Xunit;

namespace Stranichnik.Tests;

public sealed class MainWindowViewModelStorageTests
{
    [Fact]
    public void AddBookmarkToFolderStart_adds_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddBookmarkToFolderStart(
            targetFolder,
            " New Docs ",
            " https://new-docs.example.com ");

        var bookmark = Assert.IsType<BookmarkViewModel>(result.Bookmark);

        Assert.True(result.WasAdded);
        Assert.Same(bookmark, targetFolder.Children[0]);
        Assert.Same(targetFolder, bookmark.Parent);
        Assert.Equal("New Docs", bookmark.Title);
        Assert.Equal("https://new-docs.example.com", bookmark.Url);
        Assert.False(string.IsNullOrWhiteSpace(bookmark.Id));
        Assert.Equal(0, result.TargetIndex);
        Assert.Same(targetFolder, result.TargetParent);
    }

    [Fact]
    public void AddBookmarkToFolderStart_refreshes_stale_target_folder_after_tree_reload()
    {
        var viewModel = CreateViewModel();
        var staleTargetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        viewModel.ReloadVisibleTreeAndSearch();
        var currentTargetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddBookmarkToFolderStart(
            staleTargetFolder,
            "New Docs",
            "https://new-docs.example.com");

        var bookmark = Assert.IsType<BookmarkViewModel>(result.Bookmark);
        Assert.True(result.WasAdded);
        Assert.NotSame(staleTargetFolder, currentTargetFolder);
        Assert.Same(currentTargetFolder, result.TargetParent);
        Assert.Same(bookmark, currentTargetFolder.Children[0]);
        Assert.DoesNotContain(bookmark, staleTargetFolder.Children);
    }

    [Fact]
    public void AddFolderToFolderStart_adds_folder_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddFolderToFolderStart(
            targetFolder,
            " New Folder ");

        var folder = Assert.IsType<BookmarkFolderViewModel>(result.Folder);

        Assert.True(result.WasAdded);
        Assert.Same(folder, targetFolder.Children[0]);
        Assert.Same(targetFolder, folder.Parent);
        Assert.Equal("New Folder", folder.Title);
        Assert.False(folder.IsExpanded);
        Assert.False(string.IsNullOrWhiteSpace(folder.Id));
        Assert.Equal(0, result.TargetIndex);
        Assert.Same(targetFolder, result.TargetParent);
    }

    [Fact]
    public void AddFolderToFolderStart_refreshes_stale_target_folder_after_tree_reload()
    {
        var viewModel = CreateViewModel();
        var staleTargetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        viewModel.ReloadVisibleTreeAndSearch();
        var currentTargetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddFolderToFolderStart(
            staleTargetFolder,
            "New Folder");

        var folder = Assert.IsType<BookmarkFolderViewModel>(result.Folder);
        Assert.True(result.WasAdded);
        Assert.NotSame(staleTargetFolder, currentTargetFolder);
        Assert.Same(currentTargetFolder, result.TargetParent);
        Assert.Same(folder, currentTargetFolder.Children[0]);
        Assert.DoesNotContain(folder, staleTargetFolder.Children);
    }

    [Fact]
    public void EditBookmark_updates_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            targetFolder.Children.First(item => item.Id == "avalonia-docs"));

        var result = viewModel.EditBookmark(
            bookmark,
            " New Avalonia ",
            " https://new-avalonia.example.com ");

        Assert.True(result.WasEdited);
        Assert.Same(bookmark, result.Bookmark);
        Assert.Equal("Avalonia Docs", result.OldTitle);
        Assert.Equal("https://docs.avaloniaui.net/", result.OldUrl);
        Assert.Equal("New Avalonia", bookmark.Title);
        Assert.Equal("https://new-avalonia.example.com", bookmark.Url);
    }

    [Fact]
    public void EditFolder_updates_folder_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.EditFolder(folder, " New Work ");

        Assert.True(result.WasEdited);
        Assert.Same(folder, result.Folder);
        Assert.Equal("Work", result.OldTitle);
        Assert.Equal("New Work", folder.Title);
    }

    [Fact]
    public void EditFolder_rejects_root_folder()
    {
        var viewModel = CreateViewModel();
        var oldTitle = viewModel.RootFolder.Title;

        var result = viewModel.EditFolder(viewModel.RootFolder, "New Root");

        Assert.False(result.WasEdited);
        Assert.Equal(oldTitle, viewModel.RootFolder.Title);
    }

    [Fact]
    public void DeleteItem_removes_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            folder.Children.First(item => item.Id == "avalonia-docs"));

        var result = viewModel.DeleteItem(bookmark);

        Assert.True(result.WasDeleted);
        Assert.Same(bookmark, result.Item);
        Assert.Same(folder, result.SourceParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.DoesNotContain(bookmark, folder.Children);
        Assert.Null(bookmark.Parent);
    }

    [Fact]
    public void DeleteItem_removes_folder_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.DeleteItem(folder);

        Assert.True(result.WasDeleted);
        Assert.Same(folder, result.Item);
        Assert.Same(viewModel.RootFolder, result.SourceParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.DoesNotContain(folder, viewModel.Items);
        Assert.Null(folder.Parent);
    }

    [Fact]
    public void DeleteItem_rejects_root_folder()
    {
        var viewModel = CreateViewModel();

        var result = viewModel.DeleteItem(viewModel.RootFolder);

        Assert.False(result.WasDeleted);
        Assert.NotNull(viewModel.RootFolder);
    }

    [Fact]
    public void MoveItemToFolderStart_moves_bookmark_through_storage_path()
    {
        var viewModel = CreateViewModel();
        var bookmark = Assert.IsType<BookmarkViewModel>(
            viewModel.Items.First(item => item.Id == "openai"));
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.MoveItemToFolderStart(bookmark, targetFolder);

        Assert.True(result.WasMoved);
        Assert.Same(bookmark, targetFolder.Children[0]);
        Assert.Same(targetFolder, bookmark.Parent);
        Assert.DoesNotContain(bookmark, viewModel.Items);
        Assert.Same(viewModel.RootFolder, result.SourceParent);
        Assert.Same(targetFolder, result.TargetParent);
        Assert.Equal(4, result.SourceIndex);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void MoveItemToFolderStart_treats_null_target_as_root_folder()
    {
        var viewModel = CreateViewModel();
        var sourceFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            sourceFolder.Children.First(item => item.Id == "avalonia-docs"));

        var result = viewModel.MoveItemToFolderStart(bookmark, targetParent: null);

        Assert.True(result.WasMoved);
        Assert.Same(bookmark, viewModel.Items[0]);
        Assert.Same(viewModel.RootFolder, bookmark.Parent);
        Assert.Same(sourceFolder, result.SourceParent);
        Assert.Same(viewModel.RootFolder, result.TargetParent);
        Assert.Equal(0, result.SourceIndex);
        Assert.Equal(0, result.TargetIndex);
    }

    [Fact]
    public void CanMoveItemToFolder_rejects_folder_move_to_descendant()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "deep-level-1"));
        var descendant = Assert.IsType<BookmarkFolderViewModel>(
            folder.Children.First(item => item.Id == "deep-level-2"));

        Assert.False(viewModel.CanMoveItemToFolder(folder, descendant));
    }

    [Fact]
    public void Constructor_rebuilds_search_from_initial_snapshot()
    {
        var viewModel = CreateViewModel();

        var results = viewModel.SearchBookmarks("avaloniaui");

        Assert.Equal("avalonia-docs", Assert.Single(results).Id);
    }

    [Fact]
    public void AddBookmarkToFolderStart_updates_search_index()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        var result = viewModel.AddBookmarkToFolderStart(
            targetFolder,
            "Zephyr Manuals",
            "https://zephyr-manuals.example.com");

        var searchResult = Assert.Single(viewModel.SearchBookmarks("zephyr manuals"));

        Assert.True(result.WasAdded);
        Assert.Equal(result.Bookmark?.Id, searchResult.Id);
    }

    [Fact]
    public void AddSecretBookmarkToFolderStart_encrypts_storage_and_indexes_visible_projection()
    {
        var store = new InMemoryBookmarkTreeStore(SampleBookmarkRecordsFactory.Create().Items);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(
            store,
            profileStore,
            session);
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        viewModel.CreateMasterPassword("password", showSecrets: true);

        var result = viewModel.AddSecretBookmarkToFolderStart(
            targetFolder,
            "Secret Search Target",
            "https://secret.example.com");

        var bookmark = Assert.IsType<BookmarkViewModel>(result.Bookmark);
        var storageRecord = Assert.Single(store.Load().Items, item => item.Id == bookmark.Id);

        Assert.True(result.WasAdded);
        Assert.True(bookmark.IsSecret);
        Assert.Null(bookmark.IconImage);
        Assert.Equal("Secret Search Target", bookmark.Title);
        Assert.Equal("https://secret.example.com", bookmark.Url);
        Assert.True(storageRecord.IsSecret);
        Assert.Null(storageRecord.Title);
        Assert.Null(storageRecord.Url);
        Assert.NotNull(storageRecord.EncryptedPayload);
        Assert.Equal(bookmark.Id, Assert.Single(viewModel.SearchBookmarks("secret target")).Id);
    }

    [Fact]
    public void EditBookmark_secret_conversion_preserves_icon_through_view_model_path()
    {
        var originalBookmark = CreateBookmark(
            "icon-bookmark",
            parentId: null,
            "Icon Bookmark",
            "https://icon.example.com") with
        {
            IconAssetId = "plain-icon"
        };
        var store = new InMemoryBookmarkTreeStore([originalBookmark]);
        store.GetOrCreateIconAsset(CreateIconAsset("plain-icon", "same-icon-source"));
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);
        viewModel.CreateMasterPassword("password", showSecrets: true);
        var bookmark = Assert.IsType<BookmarkViewModel>(
            Assert.Single(viewModel.Items));

        var secretResult = viewModel.EditBookmarkAsSecret(
            bookmark,
            "Secret Icon Bookmark",
            "https://secret-icon.example.com",
            BookmarkIconSelection.KeepExisting);
        var secretRecord = Assert.Single(store.Load().Items, item => item.Id == "icon-bookmark");
        var secretIconAssetId = Assert.IsType<string>(secretRecord.SecretIconAssetId);

        var plaintextResult = viewModel.EditSecretBookmarkAsPlaintext(
            bookmark,
            "Plain Icon Bookmark",
            "https://plain-icon.example.com",
            BookmarkIconSelection.KeepExisting);
        var plaintextRecord = Assert.Single(store.Load().Items, item => item.Id == "icon-bookmark");

        Assert.True(secretResult.WasEdited);
        Assert.True(secretRecord.IsSecret);
        Assert.Null(secretRecord.IconAssetId);
        Assert.NotNull(store.GetSecretIconAsset(secretIconAssetId));
        Assert.True(plaintextResult.WasEdited);
        Assert.False(plaintextRecord.IsSecret);
        Assert.Equal("plain-icon", plaintextRecord.IconAssetId);
        Assert.Null(plaintextRecord.SecretIconAssetId);
        Assert.Equal("Plain Icon Bookmark", bookmark.Title);
    }

    [Fact]
    public void AddBookmarkToFolderStart_can_use_regular_library_icon()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        store.GetOrCreateIconAsset(CreateIconAsset("library-icon", "library-source"));
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);

        var result = viewModel.AddBookmarkToFolderStart(
            viewModel.RootFolder,
            "Bookmark",
            "https://example.com",
            BookmarkIconSelection.FromLibrary(new IconLibrarySelection("library-icon", SecretIconAssetId: null)));

        var record = Assert.Single(store.Load().Items);
        Assert.True(result.WasAdded);
        Assert.Equal("library-icon", record.IconAssetId);
        Assert.Null(record.SecretIconAssetId);
    }

    [Fact]
    public void AddBookmarkToFolderStart_rejects_missing_regular_library_icon()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);

        var result = viewModel.AddBookmarkToFolderStart(
            viewModel.RootFolder,
            "Bookmark",
            "https://example.com",
            BookmarkIconSelection.FromLibrary(new IconLibrarySelection("missing-icon", SecretIconAssetId: null)));

        Assert.False(result.WasAdded);
        Assert.Empty(store.Load().Items);
    }

    [Fact]
    public void AddSecretBookmarkToFolderStart_can_convert_regular_library_icon_to_secret()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        store.GetOrCreateIconAsset(CreateIconAsset("regular-icon", "same-source"));
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);
        viewModel.CreateMasterPassword("password", showSecrets: true);

        var result = viewModel.AddSecretBookmarkToFolderStart(
            viewModel.RootFolder,
            "Secret",
            "https://secret.example.com",
            BookmarkIconSelection.FromLibrary(new IconLibrarySelection("regular-icon", SecretIconAssetId: null)));

        var record = Assert.Single(store.Load().Items);
        var secretIconAsset = Assert.IsType<SecretIconAssetRecord>(store.GetSecretIconAsset(
            Assert.IsType<string>(record.SecretIconAssetId)));
        Assert.True(result.WasAdded);
        Assert.True(record.IsSecret);
        Assert.Null(record.IconAssetId);
        Assert.Equal("same-source", secretIconAsset.SourceHash);
    }

    [Fact]
    public void AddSecretBookmarkToFolderStart_rejects_missing_secret_library_icon()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);
        viewModel.CreateMasterPassword("password", showSecrets: true);

        var result = viewModel.AddSecretBookmarkToFolderStart(
            viewModel.RootFolder,
            "Secret",
            "https://secret.example.com",
            BookmarkIconSelection.FromLibrary(new IconLibrarySelection(
                RegularIconAssetId: null,
                SecretIconAssetId: "missing-secret-icon")));

        Assert.False(result.WasAdded);
        Assert.Empty(store.Load().Items);
    }

    [Fact]
    public void AddFolderToFolderStart_can_convert_secret_library_icon_to_regular()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        store.GetOrCreateIconAsset(CreateIconAsset("regular-icon", "same-source"));
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);
        viewModel.CreateMasterPassword("password", showSecrets: true);
        var secretIconAsset = CreateSecretIconAssetFromRegular(store, session, profileStore, "regular-icon");

        var result = viewModel.AddFolderToFolderStart(
            viewModel.RootFolder,
            "Folder",
            BookmarkIconSelection.FromLibrary(new IconLibrarySelection(RegularIconAssetId: null, secretIconAsset.Id)));

        var record = Assert.Single(store.Load().Items);
        Assert.True(result.WasAdded);
        Assert.Equal("regular-icon", record.IconAssetId);
        Assert.Null(record.SecretIconAssetId);
    }

    [Fact]
    public void AddSecretBookmarkToFolderStart_can_use_secret_library_icon_directly()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        store.GetOrCreateIconAsset(CreateIconAsset("regular-icon", "same-source"));
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);
        viewModel.CreateMasterPassword("password", showSecrets: true);
        var secretIconAsset = CreateSecretIconAssetFromRegular(store, session, profileStore, "regular-icon");

        var result = viewModel.AddSecretBookmarkToFolderStart(
            viewModel.RootFolder,
            "Secret",
            "https://secret.example.com",
            BookmarkIconSelection.FromLibrary(new IconLibrarySelection(RegularIconAssetId: null, secretIconAsset.Id)));

        var record = Assert.Single(store.Load().Items);
        Assert.True(result.WasAdded);
        Assert.Equal(secretIconAsset.Id, record.SecretIconAssetId);
        Assert.Null(record.IconAssetId);
    }

    [Fact]
    public void EditBookmark_updates_search_index()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            targetFolder.Children.First(item => item.Id == "avalonia-docs"));

        viewModel.EditBookmark(
            bookmark,
            "Renamed Search Target",
            "https://renamed.example.com");

        Assert.Empty(viewModel.SearchBookmarks("avaloniaui"));
        Assert.Equal("avalonia-docs", Assert.Single(viewModel.SearchBookmarks("renamed target")).Id);
    }

    [Fact]
    public void DeleteItem_removes_bookmark_from_search_index()
    {
        var viewModel = CreateViewModel();
        var targetFolder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));
        var bookmark = Assert.IsType<BookmarkViewModel>(
            targetFolder.Children.First(item => item.Id == "avalonia-docs"));

        viewModel.DeleteItem(bookmark);

        Assert.Empty(viewModel.SearchBookmarks("avaloniaui"));
    }

    [Fact]
    public void DeleteItem_removes_folder_descendants_from_search_index()
    {
        var viewModel = CreateViewModel();
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "work"));

        viewModel.DeleteItem(folder);

        Assert.Empty(viewModel.SearchBookmarks("avaloniaui"));
        Assert.Empty(viewModel.SearchBookmarks("nuget"));
    }

    [Fact]
    public void DeleteItem_rebuilds_search_from_visible_secret_projection_after_folder_delete()
    {
        using var session = CreateSecretSession(showSecrets: true, out var crypto, out var secretRecord);
        var viewModel = CreateViewModel(
            new BookmarkTreeSnapshot(
            [
                CreateFolder("normal-folder", parentId: null),
                CreateBookmark("normal", "normal-folder", "Normal Bookmark", "https://normal.example.com"),
                secretRecord
            ]),
            session,
            crypto);
        var folder = Assert.IsType<BookmarkFolderViewModel>(
            viewModel.Items.First(item => item.Id == "normal-folder"));

        viewModel.DeleteItem(folder);

        Assert.Empty(viewModel.SearchBookmarks("normal bookmark"));
        Assert.Equal("secret", Assert.Single(viewModel.SearchBookmarks("secret title")).Id);
    }

    [Fact]
    public void SearchQuery_populates_display_results()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchQuery = "github";

        var result = Assert.Single(viewModel.SearchResults);
        Assert.True(viewModel.IsSearchActive);
        Assert.False(viewModel.IsBookmarksTreeVisible);
        Assert.True(viewModel.HasSearchResults);
        Assert.False(viewModel.HasNoSearchResults);
        Assert.Equal("github", result.Id);
        Assert.Equal("GitHub", result.Title);
        Assert.Equal("https://github.com/", result.Url);
    }

    [Fact]
    public void SearchQuery_shows_empty_state_when_no_results_match()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchQuery = "zzzzzzzzzzzz";

        Assert.Empty(viewModel.SearchResults);
        Assert.True(viewModel.IsSearchActive);
        Assert.False(viewModel.IsBookmarksTreeVisible);
        Assert.False(viewModel.HasSearchResults);
        Assert.True(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void ClearSearch_clears_display_results_and_shows_tree()
    {
        var viewModel = CreateViewModel();
        viewModel.SearchQuery = "github";

        viewModel.ClearSearch();

        Assert.Empty(viewModel.SearchResults);
        Assert.False(viewModel.IsSearchActive);
        Assert.True(viewModel.IsBookmarksTreeVisible);
        Assert.False(viewModel.HasSearchResults);
        Assert.False(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void Constructor_hides_secret_bookmarks_from_tree_and_search_when_secrets_are_hidden()
    {
        using var session = CreateSecretSession(showSecrets: false, out var crypto, out var secretRecord);
        var viewModel = CreateViewModel(
            new BookmarkTreeSnapshot(
            [
                CreateFolder("secret-folder", parentId: null),
                secretRecord with
                {
                    ParentId = "secret-folder"
                },
                CreateBookmark("normal", parentId: null, "Normal Bookmark", "https://normal.example.com")
            ]),
            session,
            crypto);

        Assert.DoesNotContain(viewModel.Items, item => item.Id == "secret-folder");
        Assert.DoesNotContain(viewModel.Items, item => item.Id == "secret");
        Assert.Contains(viewModel.Items, item => item.Id == "normal");
        Assert.Empty(viewModel.SearchBookmarks("secret title"));
        Assert.Equal("normal", Assert.Single(viewModel.SearchBookmarks("normal bookmark")).Id);
    }

    [Fact]
    public void Constructor_shows_and_indexes_secret_bookmarks_when_secrets_are_visible()
    {
        using var session = CreateSecretSession(showSecrets: true, out var crypto, out var secretRecord);
        var viewModel = CreateViewModel(
            new BookmarkTreeSnapshot([secretRecord]),
            session,
            crypto);

        var bookmark = Assert.IsType<BookmarkViewModel>(Assert.Single(viewModel.Items));

        Assert.Equal("secret", bookmark.Id);
        Assert.Equal("Secret Title", bookmark.Title);
        Assert.Equal("https://secret.example.com", bookmark.Url);
        Assert.Equal("secret", Assert.Single(viewModel.SearchBookmarks("secret title")).Id);
    }

    [Fact]
    public void Secret_session_state_change_rebuilds_tree_and_current_search_results()
    {
        using var session = CreateSecretSession(showSecrets: true, out var crypto, out var secretRecord);
        var viewModel = CreateViewModel(
            new BookmarkTreeSnapshot([secretRecord]),
            session,
            crypto);
        viewModel.SearchQuery = "secret title";

        session.HideSecrets();

        Assert.Empty(viewModel.Items);
        Assert.Empty(viewModel.SearchResults);
        Assert.Empty(viewModel.SearchBookmarks("secret title"));
        Assert.True(viewModel.IsSearchActive);
        Assert.True(viewModel.HasNoSearchResults);
    }

    [Fact]
    public void UnlockSecrets_with_correct_password_shows_secret_bookmarks()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        var viewModel = new MainWindowViewModel(
            store,
            new BookmarkSearchService(new InMemoryBookmarkSearchIndex()),
            secretProfileStore: profileStore,
            secretCryptoService: crypto,
            secretSession: session,
            secretProjectionService: new SecretBookmarkProjectionService(crypto));
        var setupResult = viewModel.CreateMasterPassword("password", showSecrets: false);
        var profile = Assert.IsType<CryptoProfileRecord>(setupResult.Profile);
        var encrypted = crypto.EncryptBookmarkPayload(
            new SecretBookmarkPayloadV1("Secret Title", "https://secret.example.com"),
            Assert.IsType<RuntimeSecretKey>(session.BorrowDataKey()),
            profile.Id,
            "secret");
        store.AddSecretBookmarkToFolderStart(
            parentId: null,
            bookmarkId: "secret",
            encryptedPayload: new EncryptedBookmarkPayloadRecord(
                encrypted.Payload,
                encrypted.Nonce,
                encrypted.CryptoProfileId,
                encrypted.PayloadFormatVersion));
        session.LockAndForgetKey();

        var result = viewModel.UnlockSecrets("password", showSecrets: true);

        Assert.True(result.WasUnlocked);
        Assert.True(viewModel.IsSecretSessionUnlocked);
        Assert.True(viewModel.AreSecretsVisible);
        Assert.Contains(viewModel.Items, item => item.Id == "secret");
    }

    [Fact]
    public void HideSecretsByUserAction_hides_visible_secrets_without_locking_session()
    {
        using var session = CreateSecretSession(showSecrets: true, out var crypto, out var secretRecord);
        var viewModel = CreateViewModel(
            new BookmarkTreeSnapshot([secretRecord]),
            session,
            crypto);

        viewModel.HideSecretsByUserAction();

        Assert.True(viewModel.IsSecretSessionUnlocked);
        Assert.False(viewModel.AreSecretsVisible);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public void HideSecretsByInactivityTimeout_hides_visible_secrets_without_locking_session()
    {
        using var session = CreateSecretSession(showSecrets: true, out var crypto, out var secretRecord);
        var viewModel = CreateViewModel(
            new BookmarkTreeSnapshot([secretRecord]),
            session,
            crypto);

        viewModel.HideSecretsByInactivityTimeout();

        Assert.True(viewModel.IsSecretSessionUnlocked);
        Assert.False(viewModel.AreSecretsVisible);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public void SaveSecretMasterPassword_creates_profile_when_missing()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);

        var result = viewModel.SaveSecretMasterPassword("password");

        Assert.True(result.Succeeded);
        Assert.True(result.WasCreated);
        Assert.True(viewModel.IsSecretProfileConfigured);
        Assert.True(viewModel.IsSecretSessionUnlocked);
        Assert.False(viewModel.AreSecretsVisible);
    }

    [Fact]
    public void SaveSecretMasterPassword_changes_profile_when_unlocked()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var crypto = new SecretCryptoService(TestPbkdf2Iterations);
        var viewModel = new MainWindowViewModel(
            store,
            new BookmarkSearchService(new InMemoryBookmarkSearchIndex()),
            secretProfileStore: profileStore,
            secretCryptoService: crypto,
            secretSession: session,
            secretProjectionService: new SecretBookmarkProjectionService(crypto));
        viewModel.CreateMasterPassword("old password", showSecrets: false);

        var result = viewModel.SaveSecretMasterPassword("new password");

        var profile = Assert.IsType<CryptoProfileRecord>(profileStore.LoadActiveProfile());
        using var newUnlockKey = Assert.IsType<RuntimeSecretKey>(crypto.Unlock(profile, "new password").DataKey);
        Assert.True(result.Succeeded);
        Assert.False(result.WasCreated);
        Assert.False(crypto.Unlock(profile, "old password").IsSuccess);
        Assert.False(newUnlockKey.IsDisposed);
    }

    [Fact]
    public void SaveSecretMasterPassword_requires_unlock_for_existing_profile()
    {
        var store = new InMemoryBookmarkTreeStore([]);
        var profileStore = new InMemorySecretProfileStore();
        using var session = new SecretSessionService();
        var viewModel = CreateViewModel(store, profileStore, session);
        viewModel.CreateMasterPassword("old password", showSecrets: false);
        session.LockAndForgetKey();

        var result = viewModel.SaveSecretMasterPassword("new password");

        Assert.False(result.Succeeded);
        Assert.Equal(SecretPasswordSaveFailureReason.UnlockRequired, result.FailureReason);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        return CreateViewModel(
            SampleBookmarkRecordsFactory.Create(),
            secretSession: null,
            secretCryptoService: null);
    }

    private static MainWindowViewModel CreateViewModel(
        BookmarkTreeSnapshot snapshot,
        ISecretSessionService? secretSession,
        ISecretCryptoService? secretCryptoService,
        IReadOnlySet<string>? expandedFolderIds = null)
    {
        var store = new InMemoryBookmarkTreeStore(snapshot.Items);
        var cryptoService = secretCryptoService ?? new SecretCryptoService(TestPbkdf2Iterations);

        return new MainWindowViewModel(
            store,
            new BookmarkSearchService(new InMemoryBookmarkSearchIndex()),
            expandedFolderIds: expandedFolderIds,
            secretCryptoService: cryptoService,
            secretSession: secretSession,
            secretProjectionService: new SecretBookmarkProjectionService(cryptoService));
    }

    private static MainWindowViewModel CreateViewModel(
        InMemoryBookmarkTreeStore store,
        ISecretProfileStore secretProfileStore,
        ISecretSessionService secretSession)
    {
        var cryptoService = new SecretCryptoService(TestPbkdf2Iterations);

        return new MainWindowViewModel(
            store,
            new BookmarkSearchService(new InMemoryBookmarkSearchIndex()),
            secretProfileStore: secretProfileStore,
            secretCryptoService: cryptoService,
            secretSession: secretSession,
            secretProjectionService: new SecretBookmarkProjectionService(cryptoService));
    }

    private static SecretSessionService CreateSecretSession(
        bool showSecrets,
        out SecretCryptoService crypto,
        out BookmarkItemRecord secretRecord)
    {
        crypto = new SecretCryptoService(TestPbkdf2Iterations);
        var created = crypto.CreateProfile("password", Now);
        var session = new SecretSessionService();
        session.ConfigureAndUnlock(created.DataKey, showSecrets);
        var encrypted = crypto.EncryptBookmarkPayload(
            new SecretBookmarkPayloadV1("Secret Title", "https://secret.example.com"),
            Assert.IsType<RuntimeSecretKey>(session.BorrowDataKey()),
            created.Profile.Id,
            "secret");
        secretRecord = new BookmarkItemRecord(
            "secret",
            ParentId: null,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            Title: null,
            Url: null,
            IsSecret: true,
            new EncryptedBookmarkPayloadRecord(
                encrypted.Payload,
                encrypted.Nonce,
                encrypted.CryptoProfileId,
                encrypted.PayloadFormatVersion),
            CreateMetadata());

        return session;
    }

    private static BookmarkItemRecord CreateFolder(string id, string? parentId)
    {
        return new BookmarkItemRecord(
            id,
            parentId,
            BookmarkItemKind.Folder,
            SortOrder: 1000,
            $"Folder {id}",
            Url: null,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkItemRecord CreateBookmark(
        string id,
        string? parentId,
        string title,
        string url)
    {
        return new BookmarkItemRecord(
            id,
            parentId,
            BookmarkItemKind.Bookmark,
            SortOrder: 1000,
            title,
            url,
            IsSecret: false,
            EncryptedPayload: null,
            CreateMetadata());
    }

    private static BookmarkIconAssetRecord CreateIconAsset(string id, string sourceHash)
    {
        return new(
            id,
            IconHash.Sha256Algorithm,
            sourceHash,
            SourceSizeBytes: 3,
            "image/png",
            ProcessedWidth: 64,
            ProcessedHeight: 64,
            ProcessedIconBytes,
            Now);
    }

    private static SecretIconAssetRecord CreateSecretIconAssetFromRegular(
        InMemoryBookmarkTreeStore store,
        SecretSessionService session,
        InMemorySecretProfileStore profileStore,
        string iconAssetId)
    {
        var profile = Assert.IsType<CryptoProfileRecord>(profileStore.LoadActiveProfile());
        var dataKey = Assert.IsType<RuntimeSecretKey>(session.BorrowDataKey());
        var service = new SecretIconAssetService(store, new IconImageProcessor());

        return Assert.IsType<SecretIconAssetRecord>(service.GetOrCreateFromRegularIconAsset(
            iconAssetId,
            dataKey,
            profile.SecretGenerationId));
    }

    private static BookmarkItemMetadata CreateMetadata()
    {
        return new BookmarkItemMetadata(
            Now,
            Now,
            DeletedAtUtc: null,
            Revision: 1,
            BookmarkSyncState.Clean,
            RemoteEtag: null,
            LastSyncedAtUtc: null,
            ModifiedDeviceId: "test");
    }

    private const int TestPbkdf2Iterations = 1000;
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] ProcessedIconBytes = [1, 2, 3];
}
