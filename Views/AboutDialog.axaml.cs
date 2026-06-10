using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Stranichnik.Diagnostics;
using Stranichnik.Localization;

namespace Stranichnik.Views;

public sealed partial class AboutDialog : Window
{
    private const string ProjectLicenseFileName = "LICENSE";
    private const string ThirdPartyNoticesFileName = "THIRD_PARTY_NOTICES.md";
    private const string ThirdPartyLicensesDirectoryName = "THIRD_PARTY_LICENSES";

    private readonly List<Button> _documentButtons = [];

    public AboutDialog()
    {
        InitializeComponent();
        Title = UiStrings.AboutDialogTitle;
        LoadDocuments();
        Opened += (_, _) => AboutScrollViewer.Offset = default;
    }

    public static Task<object?> ShowAsync(Window owner)
    {
        var dialog = new AboutDialog();
        return dialog.ShowDialog<object?>(owner);
    }

    private void LoadDocuments()
    {
        ProjectLicenseTextBlock.Text = ReadTextDocument(ProjectLicenseFileName);

        var documents = LoadThirdPartyDocuments();
        DocumentButtonsPanel.Children.Clear();
        _documentButtons.Clear();

        if (documents.Count == 0)
        {
            SelectedDocumentTitleTextBlock.Text = UiStrings.AboutNoThirdPartyDocumentsTitle;
            SelectedDocumentLinesListBox.ItemsSource = new[] { UiStrings.AboutNoThirdPartyDocumentsMessage };
            Logs.Print("About dialog loaded without third-party documents.");
            return;
        }

        foreach (var document in documents)
        {
            var button = new Button
            {
                Tag = document,
                Content = new TextBlock
                {
                    Text = document.ButtonLabel,
                    TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                }
            };

            button.Classes.Add("aboutDocumentButton");
            button.Click += OnDocumentButtonClick;
            DocumentButtonsPanel.Children.Add(button);
            _documentButtons.Add(button);
        }

        SelectDocument(documents[0]);
        Logs.Print($"About dialog loaded third-party documents. Count={documents.Count}.");
    }

    private static List<AboutDocument> LoadThirdPartyDocuments()
    {
        var documents = new List<AboutDocument>();

        var thirdPartyNoticesPath = ResolveDocumentPath(ThirdPartyNoticesFileName);
        if (File.Exists(thirdPartyNoticesPath))
        {
            documents.Add(new AboutDocument(
                ThirdPartyNoticesFileName,
                Path.GetFileName(ThirdPartyNoticesFileName),
                ReadTextDocumentLines(ThirdPartyNoticesFileName)));
        }

        var licensesDirectoryPath = ResolveDocumentPath(ThirdPartyLicensesDirectoryName);
        if (!Directory.Exists(licensesDirectoryPath))
            return documents;

        var licenseFiles = Directory.EnumerateFiles(licensesDirectoryPath)
            .Where(IsReadableLicenseDocument)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

        foreach (var licenseFile in licenseFiles)
        {
            var relativeName = Path.Combine(ThirdPartyLicensesDirectoryName, Path.GetFileName(licenseFile));
            documents.Add(new AboutDocument(
                relativeName,
                Path.GetFileName(licenseFile),
                ReadTextDocumentLines(relativeName)));
        }

        return documents;
    }

    private static bool IsReadableLicenseDocument(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.Equals(fileName, "README.md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "NUGET_LICENSE_METADATA.md", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    private void OnDocumentButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AboutDocument document })
            SelectDocument(document);
    }

    private void SelectDocument(AboutDocument document)
    {
        SelectedDocumentTitleTextBlock.Text = document.DisplayName;
        SelectedDocumentLinesListBox.ItemsSource = document.Lines;

        foreach (var button in _documentButtons)
            button.Classes.Set("selected", Equals(button.Tag, document));
    }

    private static string ReadTextDocument(string relativePath)
    {
        var path = ResolveDocumentPath(relativePath);
        if (!File.Exists(path))
            return UiStrings.AboutDocumentMissing;

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            Logs.Print("About dialog failed to read a license document: IO error.");
            return UiStrings.AboutDocumentReadFailed;
        }
        catch (UnauthorizedAccessException)
        {
            Logs.Print("About dialog failed to read a license document: access denied.");
            return UiStrings.AboutDocumentReadFailed;
        }
    }

    private static string[] ReadTextDocumentLines(string relativePath)
    {
        var path = ResolveDocumentPath(relativePath);
        if (!File.Exists(path))
            return [UiStrings.AboutDocumentMissing];

        try
        {
            return File.ReadLines(path).ToArray();
        }
        catch (IOException)
        {
            Logs.Print("About dialog failed to read a license document: IO error.");
            return [UiStrings.AboutDocumentReadFailed];
        }
        catch (UnauthorizedAccessException)
        {
            Logs.Print("About dialog failed to read a license document: access denied.");
            return [UiStrings.AboutDocumentReadFailed];
        }
    }

    private static string ResolveDocumentPath(string relativePath)
    {
        return Path.Combine(AppContext.BaseDirectory, relativePath);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    private sealed record AboutDocument(
        string DisplayName,
        string ButtonLabel,
        string[] Lines);
}
