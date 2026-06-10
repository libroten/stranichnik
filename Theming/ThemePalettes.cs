using System.Linq;
using Avalonia.Media;

namespace Stranichnik.Theming;

public static class ThemePalettes
{
    public static ThemePalette Light { get; } = Create(
        (ThemeResourceKeys.AppBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.AppTopBarBackgroundBrush, "#FDFDFD"),
        (ThemeResourceKeys.AppTopBarBorderBrush, "#E5DFD8"),
        (ThemeResourceKeys.WorkAreaBackgroundBrush, "#FDFDFD"),
        (ThemeResourceKeys.WorkAreaBorderBrush, "#E2DCD5"),
        (ThemeResourceKeys.SurfaceBrush, "#FDFDFD"),
        (ThemeResourceKeys.SurfaceMutedBrush, "#F1EDE8"),
        (ThemeResourceKeys.SurfaceRaisedBrush, "#FFFFFF"),
        (ThemeResourceKeys.SeparatorBrush, "#E5DFD8"),

        (ThemeResourceKeys.TextPrimaryBrush, "#202336"),
        (ThemeResourceKeys.TextSecondaryBrush, "#596073"),
        (ThemeResourceKeys.TextMutedBrush, "#7D8290"),
        (ThemeResourceKeys.TextOnAccentBrush, "#FFFFFF"),
        (ThemeResourceKeys.TextErrorBrush, "#4F587F"),
        (ThemeResourceKeys.PlaceholderTextBrush, "#9EA3AE"),
        (ThemeResourceKeys.CaretBrush, "#202336"),

        (ThemeResourceKeys.HoverBackgroundBrush, "#ECEFF7"),
        (ThemeResourceKeys.PressedBackgroundBrush, "#DDE2F0"),
        (ThemeResourceKeys.FocusBorderBrush, "#35416D"),
        (ThemeResourceKeys.SelectedBackgroundBrush, "#E3E7F3"),

        (ThemeResourceKeys.FolderRowBackgroundBrush, "#EEE4DA"),
        (ThemeResourceKeys.FolderRowHoverBackgroundBrush, "#EEE4DA"),
        (ThemeResourceKeys.FolderContainerBorderBrush, "#EEE4DA"),
        (ThemeResourceKeys.TreeRowHoverBackgroundBrush, "#F7F3EF"),
        (ThemeResourceKeys.DragSourceBackgroundBrush, "#E3E7F3"),
        (ThemeResourceKeys.DropPlaceholderBackgroundBrush, "#FDFDFD"),
        (ThemeResourceKeys.DropPlaceholderHoverBackgroundBrush, "#E3E7F3"),
        (ThemeResourceKeys.DropPlaceholderBorderBrush, "#35416D"),
        (ThemeResourceKeys.GlyphBrush, "#596073"),

        (ThemeResourceKeys.SearchBoxBackgroundBrush, "#F7F3EF"),
        (ThemeResourceKeys.SearchBoxBorderBrush, "#E5DFD8"),
        (ThemeResourceKeys.SearchIconBrush, "#35416D"),
        (ThemeResourceKeys.SearchClearButtonForegroundBrush, "#596073"),
        (ThemeResourceKeys.SearchEmptyTextBrush, "#7D8290"),

        (ThemeResourceKeys.UrlTextBrush, "#596073"),
        (ThemeResourceKeys.UrlHoverTextBrush, "#35416D"),
        (ThemeResourceKeys.UrlUnderlineBrush, "#35416D"),

        (ThemeResourceKeys.ActionOpenBrush, "#35416D"),
        (ThemeResourceKeys.ActionOpenHoverBackgroundBrush, "#E3E7F3"),
        (ThemeResourceKeys.ActionAddBookmarkBrush, "#35416D"),
        (ThemeResourceKeys.ActionAddBookmarkHoverBackgroundBrush, "#E3E7F3"),
        (ThemeResourceKeys.ActionAddFolderBrush, "#35416D"),
        (ThemeResourceKeys.ActionAddFolderHoverBackgroundBrush, "#E3E7F3"),
        (ThemeResourceKeys.ActionEditBrush, "#35416D"),
        (ThemeResourceKeys.ActionEditHoverBackgroundBrush, "#E3E7F3"),
        (ThemeResourceKeys.ActionDeleteBrush, "#4F587F"),
        (ThemeResourceKeys.ActionDeleteHoverBackgroundBrush, "#E6E9F2"),

        (ThemeResourceKeys.DialogBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.DialogTextPrimaryBrush, "#202336"),
        (ThemeResourceKeys.DialogTextSecondaryBrush, "#596073"),
        (ThemeResourceKeys.DialogInputBackgroundBrush, "#F7F3EF"),
        (ThemeResourceKeys.DialogInputBorderBrush, "#E5DFD8"),
        (ThemeResourceKeys.DialogButtonBackgroundBrush, "#FDFDFD"),
        (ThemeResourceKeys.DialogButtonHoverBackgroundBrush, "#F7F3EF"),
        (ThemeResourceKeys.DialogButtonBorderBrush, "#E5DFD8"),
        (ThemeResourceKeys.DialogAccentButtonBackgroundBrush, "#35416D"),
        (ThemeResourceKeys.DialogAccentButtonHoverBackgroundBrush, "#2E385F"),
        (ThemeResourceKeys.DialogAccentButtonFocusBackgroundBrush, "#4F587F"),
        (ThemeResourceKeys.DialogDangerButtonBackgroundBrush, "#4F587F"),
        (ThemeResourceKeys.DialogDangerButtonHoverBackgroundBrush, "#414A72"));

    public static ThemePalette Dark { get; } = Create(
        (ThemeResourceKeys.AppBackgroundBrush, "#242631"),
        (ThemeResourceKeys.AppTopBarBackgroundBrush, "#232735"),
        (ThemeResourceKeys.AppTopBarBorderBrush, "#343847"),
        (ThemeResourceKeys.WorkAreaBackgroundBrush, "#30333E"),
        (ThemeResourceKeys.WorkAreaBorderBrush, "#3B404F"),
        (ThemeResourceKeys.SurfaceBrush, "#30333E"),
        (ThemeResourceKeys.SurfaceMutedBrush, "#2D3240"),
        (ThemeResourceKeys.SurfaceRaisedBrush, "#333846"),
        (ThemeResourceKeys.SeparatorBrush, "#3B404F"),

        (ThemeResourceKeys.TextPrimaryBrush, "#FFF7EF"),
        (ThemeResourceKeys.TextSecondaryBrush, "#D9CFC5"),
        (ThemeResourceKeys.TextMutedBrush, "#AFA59C"),
        (ThemeResourceKeys.TextOnAccentBrush, "#242631"),
        (ThemeResourceKeys.TextErrorBrush, "#C6BDAA"),
        (ThemeResourceKeys.PlaceholderTextBrush, "#827A74"),
        (ThemeResourceKeys.CaretBrush, "#FFF7EF"),

        (ThemeResourceKeys.HoverBackgroundBrush, "#343948"),
        (ThemeResourceKeys.PressedBackgroundBrush, "#404656"),
        (ThemeResourceKeys.FocusBorderBrush, "#A39B88"),
        (ThemeResourceKeys.SelectedBackgroundBrush, "#565344"),

        (ThemeResourceKeys.FolderRowBackgroundBrush, "#444654"),
        (ThemeResourceKeys.FolderRowHoverBackgroundBrush, "#505263"),
        (ThemeResourceKeys.FolderContainerBorderBrush, "#444654"),
        (ThemeResourceKeys.TreeRowHoverBackgroundBrush, "#505263"),
        (ThemeResourceKeys.DragSourceBackgroundBrush, "#565344"),
        (ThemeResourceKeys.DropPlaceholderBackgroundBrush, "#252936"),
        (ThemeResourceKeys.DropPlaceholderHoverBackgroundBrush, "#565344"),
        (ThemeResourceKeys.DropPlaceholderBorderBrush, "#A39B88"),
        (ThemeResourceKeys.GlyphBrush, "#CFC6BE"),

        (ThemeResourceKeys.SearchBoxBackgroundBrush, "#444654"),
        (ThemeResourceKeys.SearchBoxBorderBrush, "#3F4554"),
        (ThemeResourceKeys.SearchIconBrush, "#C6BDAA"),
        (ThemeResourceKeys.SearchClearButtonForegroundBrush, "#D9CFC5"),
        (ThemeResourceKeys.SearchEmptyTextBrush, "#AFA59C"),

        (ThemeResourceKeys.UrlTextBrush, "#B6ACA4"),
        (ThemeResourceKeys.UrlHoverTextBrush, "#C6BDAA"),
        (ThemeResourceKeys.UrlUnderlineBrush, "#C6BDAA"),

        (ThemeResourceKeys.ActionOpenBrush, "#C6BDAA"),
        (ThemeResourceKeys.ActionOpenHoverBackgroundBrush, "#565344"),
        (ThemeResourceKeys.ActionAddBookmarkBrush, "#C6BDAA"),
        (ThemeResourceKeys.ActionAddBookmarkHoverBackgroundBrush, "#565344"),
        (ThemeResourceKeys.ActionAddFolderBrush, "#C6BDAA"),
        (ThemeResourceKeys.ActionAddFolderHoverBackgroundBrush, "#565344"),
        (ThemeResourceKeys.ActionEditBrush, "#C6BDAA"),
        (ThemeResourceKeys.ActionEditHoverBackgroundBrush, "#565344"),
        (ThemeResourceKeys.ActionDeleteBrush, "#C6BDAA"),
        (ThemeResourceKeys.ActionDeleteHoverBackgroundBrush, "#5F5749"),

        (ThemeResourceKeys.DialogBackgroundBrush, "#242631"),
        (ThemeResourceKeys.DialogTextPrimaryBrush, "#FFF7EF"),
        (ThemeResourceKeys.DialogTextSecondaryBrush, "#D9CFC5"),
        (ThemeResourceKeys.DialogInputBackgroundBrush, "#444654"),
        (ThemeResourceKeys.DialogInputBorderBrush, "#444654"),
        (ThemeResourceKeys.DialogButtonBackgroundBrush, "#30333E"),
        (ThemeResourceKeys.DialogButtonHoverBackgroundBrush, "#444654"),
        (ThemeResourceKeys.DialogButtonBorderBrush, "#444654"),
        (ThemeResourceKeys.DialogAccentButtonBackgroundBrush, "#A39B88"),
        (ThemeResourceKeys.DialogAccentButtonHoverBackgroundBrush, "#B4AC9A"),
        (ThemeResourceKeys.DialogAccentButtonFocusBackgroundBrush, "#C2BAA8"),
        (ThemeResourceKeys.DialogDangerButtonBackgroundBrush, "#A39B88"),
        (ThemeResourceKeys.DialogDangerButtonHoverBackgroundBrush, "#B4AC9A"));

    private static ThemePalette Create(params (string Key, string Color)[] colors)
    {
        return new ThemePalette(colors.ToDictionary(
            color => color.Key,
            color => Color.Parse(color.Color)));
    }
}
