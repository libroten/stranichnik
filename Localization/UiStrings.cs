namespace Stranichnik.Localization;

public static class UiStrings
{
    public static string ActionAddBookmark => TextResources.Get("Action_AddBookmark");
    public static string ActionAddFolder => TextResources.Get("Action_AddFolder");
    public static string ActionDelete => TextResources.Get("Action_Delete");
    public static string ActionEdit => TextResources.Get("Action_Edit");
    public static string ActionOpen => TextResources.Get("Action_Open");
    public static string BookmarkEditorAddBookmarkTitle => TextResources.Get("BookmarkEditor_AddBookmarkTitle");
    public static string BookmarkEditorAddFolderTitle => TextResources.Get("BookmarkEditor_AddFolderTitle");
    public static string BookmarkEditorEditBookmarkTitle => TextResources.Get("BookmarkEditor_EditBookmarkTitle");
    public static string BookmarkEditorEditFolderTitle => TextResources.Get("BookmarkEditor_EditFolderTitle");
    public static string BookmarkEditorTitleLabel => TextResources.Get("BookmarkEditor_TitleLabel");
    public static string BookmarkEditorUrlLabel => TextResources.Get("BookmarkEditor_UrlLabel");
    public static string BookmarkEditorUrlPlaceholder => TextResources.Get("BookmarkEditor_UrlPlaceholder");
    public static string CommonAdd => TextResources.Get("Common_Add");
    public static string CommonCancel => TextResources.Get("Common_Cancel");
    public static string CommonDelete => TextResources.Get("Common_Delete");
    public static string CommonMessage => TextResources.Get("Common_Message");
    public static string CommonOk => TextResources.Get("Common_OK");
    public static string CommonSave => TextResources.Get("Common_Save");
    public static string ConfirmDeleteBookmarkTitle => TextResources.Get("Confirm_DeleteBookmarkTitle");
    public static string ConfirmDeleteFolderTitle => TextResources.Get("Confirm_DeleteFolderTitle");
    public static string ConfirmTitle => TextResources.Get("Confirm_Title");
    public static string ErrorCannotLaunchBrowser => TextResources.Get("Error_CannotLaunchBrowser");
    public static string ErrorInvalidBookmarkUrl => TextResources.Get("Error_InvalidBookmarkUrl");
    public static string ErrorOpenPageTitle => TextResources.Get("Error_OpenPageTitle");
    public static string ErrorUnsupportedBookmarkUrlScheme => TextResources.Get("Error_UnsupportedBookmarkUrlScheme");
    public static string ErrorUrlBrowserStartFailed => TextResources.Get("Error_UrlBrowserStartFailed");
    public static string LanguageDialogEnglish => TextResources.Get("LanguageDialog_English");
    public static string LanguageDialogRestartMessage => TextResources.Get("LanguageDialog_RestartMessage");
    public static string LanguageDialogRestartTitle => TextResources.Get("LanguageDialog_RestartTitle");
    public static string LanguageDialogRussian => TextResources.Get("LanguageDialog_Russian");
    public static string LanguageDialogTitle => TextResources.Get("LanguageDialog_Title");
    public static string MainMenuApp => TextResources.Get("MainMenu_App");
    public static string MainMenuAppAbout => TextResources.Get("MainMenu_App_About");
    public static string MainMenuAppExit => TextResources.Get("MainMenu_App_Exit");
    public static string MainMenuAppSettings => TextResources.Get("MainMenu_App_Settings");
    public static string MainMenuService => TextResources.Get("MainMenu_Service");
    public static string MainMenuServiceAppearance => TextResources.Get("MainMenu_Service_Appearance");
    public static string MainMenuServiceLanguage => TextResources.Get("MainMenu_Service_Language");
    public static string RootAllBookmarks => TextResources.Get("Root_AllBookmarks");
    public static string SearchNoMatches => TextResources.Get("Search_NoMatches");
    public static string SearchPlaceholder => TextResources.Get("Search_Placeholder");
    public static string ValidationTitleRequired => TextResources.Get("Validation_TitleRequired");
    public static string ValidationUrlRequired => TextResources.Get("Validation_UrlRequired");

    public static string ConfirmDeleteBookmarkMessage(string title)
    {
        return TextResources.Format("Confirm_DeleteBookmarkMessage", title);
    }

    public static string ConfirmDeleteFolderMessage(string title)
    {
        return TextResources.Format("Confirm_DeleteFolderMessage", title);
    }
}
