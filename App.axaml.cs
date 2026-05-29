using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Stranichnik.Localization;
using Stranichnik.Settings;
using Stranichnik.Storage;
using Stranichnik.Storage.Sqlite;
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    SqliteBookmarkTreeStoreFactory.CreateDefault(AppStartupOptions.UseSampleData),
                    SampleBookmarkRecordsFactory.CreateDefaultExpandedFolderIds()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
