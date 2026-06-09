using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Stranichnik.Icons;
using Stranichnik.Localization;
using Stranichnik.Search;
using Stranichnik.Searching;
using Stranichnik.Security;
using Stranichnik.Settings;
using Stranichnik.Storage;
using Stranichnik.Storage.Sqlite;
using Stranichnik.Theming;
using Stranichnik.ViewModels;
using Stranichnik.Views;

namespace Stranichnik;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettingsService.Load();
        if (!string.IsNullOrWhiteSpace(settings.Language))
            LanguageService.Apply(settings.Language);

        ThemeService.Apply(settings.Theme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var treeStore = SqliteBookmarkTreeStoreFactory.CreateDefault(AppStartupOptions.UseSampleData);
            var secretConnectionFactory = new SqliteConnectionFactory(AppDataPaths.DatabasePath);
            var secretProfileStore = new SqliteSecretProfileStore(secretConnectionFactory);
            var secretResetStore = new SqliteSecretResetStore(
                secretConnectionFactory,
                resetDeviceId: new SqliteDatabaseMigrator(secretConnectionFactory).GetMetadataValue("device_id"));
            var secretCryptoService = new SecretCryptoService();
            var secretSession = new SecretSessionService();

            if (secretProfileStore.LoadActiveProfile() is null)
                secretSession.MarkNotConfigured();
            else
                secretSession.MarkConfiguredLocked();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    treeStore,
                    new BookmarkSearchService(new InMemoryBookmarkSearchIndex()),
                    new BookmarkIconImageCache(treeStore, secretSession),
                    SampleBookmarkRecordsFactory.CreateDefaultExpandedFolderIds(),
                    secretProfileStore,
                    secretCryptoService,
                    secretSession,
                    secretResetStore: secretResetStore),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
