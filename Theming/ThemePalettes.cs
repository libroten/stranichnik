using System.Linq;
using Avalonia.Media;

namespace Stranichnik.Theming;

public static class ThemePalettes
{
    public static ThemePalette Light { get; } = Create(
        (ThemeResourceKeys.AppBackgroundBrush, "#F7F8FA"),
        (ThemeResourceKeys.AppTopBarBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.AppTopBarBorderBrush, "#E2E5EA"),
        (ThemeResourceKeys.WorkAreaBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.WorkAreaBorderBrush, "#E2E5EA"),
        (ThemeResourceKeys.SurfaceBrush, "#FFFFFF"),
        (ThemeResourceKeys.SurfaceMutedBrush, "#F8FAFC"),
        (ThemeResourceKeys.SurfaceRaisedBrush, "#FFFFFF"),
        (ThemeResourceKeys.SeparatorBrush, "#E2E5EA"),

        (ThemeResourceKeys.TextPrimaryBrush, "#1F2937"),
        (ThemeResourceKeys.TextSecondaryBrush, "#526071"),
        (ThemeResourceKeys.TextMutedBrush, "#667085"),
        (ThemeResourceKeys.TextOnAccentBrush, "#FFFFFF"),
        (ThemeResourceKeys.TextErrorBrush, "#DC2626"),
        (ThemeResourceKeys.PlaceholderTextBrush, "#94A3B8"),
        (ThemeResourceKeys.CaretBrush, "#1F2937"),

        (ThemeResourceKeys.HoverBackgroundBrush, "#EEF6FF"),
        (ThemeResourceKeys.PressedBackgroundBrush, "#DBEAFE"),
        (ThemeResourceKeys.FocusBorderBrush, "#2563EB"),
        (ThemeResourceKeys.SelectedBackgroundBrush, "#DBEAFE"),

        (ThemeResourceKeys.FolderRowBackgroundBrush, "#EEF2F7"),
        (ThemeResourceKeys.FolderRowHoverBackgroundBrush, "#EEF6FF"),
        (ThemeResourceKeys.FolderContainerBorderBrush, "#EEF2F7"),
        (ThemeResourceKeys.TreeRowHoverBackgroundBrush, "#EEF6FF"),
        (ThemeResourceKeys.DragSourceBackgroundBrush, "#FEF3C7"),
        (ThemeResourceKeys.DropPlaceholderBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.DropPlaceholderHoverBackgroundBrush, "#DBEAFE"),
        (ThemeResourceKeys.DropPlaceholderBorderBrush, "#64748B"),
        (ThemeResourceKeys.GlyphBrush, "#64748B"),

        (ThemeResourceKeys.SearchBoxBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.SearchBoxBorderBrush, "#CBD5E1"),
        (ThemeResourceKeys.SearchIconBrush, "#94A3B8"),
        (ThemeResourceKeys.SearchClearButtonForegroundBrush, "#667085"),
        (ThemeResourceKeys.SearchEmptyTextBrush, "#667085"),

        (ThemeResourceKeys.UrlTextBrush, "#667085"),
        (ThemeResourceKeys.UrlHoverTextBrush, "#2563EB"),
        (ThemeResourceKeys.UrlUnderlineBrush, "#2563EB"),

        (ThemeResourceKeys.ActionOpenBrush, "#2563EB"),
        (ThemeResourceKeys.ActionOpenHoverBackgroundBrush, "#E6F0FF"),
        (ThemeResourceKeys.ActionAddBookmarkBrush, "#16A34A"),
        (ThemeResourceKeys.ActionAddBookmarkHoverBackgroundBrush, "#F0FDF4"),
        (ThemeResourceKeys.ActionAddFolderBrush, "#0284C7"),
        (ThemeResourceKeys.ActionAddFolderHoverBackgroundBrush, "#E0F2FE"),
        (ThemeResourceKeys.ActionEditBrush, "#CA8A04"),
        (ThemeResourceKeys.ActionEditHoverBackgroundBrush, "#FFFBEB"),
        (ThemeResourceKeys.ActionDeleteBrush, "#DC2626"),
        (ThemeResourceKeys.ActionDeleteHoverBackgroundBrush, "#FEF2F2"),

        (ThemeResourceKeys.DialogBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.DialogTextPrimaryBrush, "#1F2937"),
        (ThemeResourceKeys.DialogTextSecondaryBrush, "#526071"),
        (ThemeResourceKeys.DialogInputBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.DialogInputBorderBrush, "#CBD5E1"),
        (ThemeResourceKeys.DialogButtonBackgroundBrush, "#FFFFFF"),
        (ThemeResourceKeys.DialogButtonHoverBackgroundBrush, "#F8FAFC"),
        (ThemeResourceKeys.DialogButtonBorderBrush, "#CBD5E1"),
        (ThemeResourceKeys.DialogAccentButtonBackgroundBrush, "#2563EB"),
        (ThemeResourceKeys.DialogAccentButtonHoverBackgroundBrush, "#1D4ED8"),
        (ThemeResourceKeys.DialogAccentButtonFocusBackgroundBrush, "#3B82F6"),
        (ThemeResourceKeys.DialogDangerButtonBackgroundBrush, "#DC2626"),
        (ThemeResourceKeys.DialogDangerButtonHoverBackgroundBrush, "#B91C1C"));

    public static ThemePalette Dark { get; } = Create(
        (ThemeResourceKeys.AppBackgroundBrush, "#111827"),
        (ThemeResourceKeys.AppTopBarBackgroundBrush, "#172033"),
        (ThemeResourceKeys.AppTopBarBorderBrush, "#2D3748"),
        (ThemeResourceKeys.WorkAreaBackgroundBrush, "#151F2E"),
        (ThemeResourceKeys.WorkAreaBorderBrush, "#334155"),
        (ThemeResourceKeys.SurfaceBrush, "#151F2E"),
        (ThemeResourceKeys.SurfaceMutedBrush, "#1E293B"),
        (ThemeResourceKeys.SurfaceRaisedBrush, "#243244"),
        (ThemeResourceKeys.SeparatorBrush, "#334155"),

        (ThemeResourceKeys.TextPrimaryBrush, "#E5E7EB"),
        (ThemeResourceKeys.TextSecondaryBrush, "#CBD5E1"),
        (ThemeResourceKeys.TextMutedBrush, "#94A3B8"),
        (ThemeResourceKeys.TextOnAccentBrush, "#FFFFFF"),
        (ThemeResourceKeys.TextErrorBrush, "#F87171"),
        (ThemeResourceKeys.PlaceholderTextBrush, "#64748B"),
        (ThemeResourceKeys.CaretBrush, "#E5E7EB"),

        (ThemeResourceKeys.HoverBackgroundBrush, "#1E3A5F"),
        (ThemeResourceKeys.PressedBackgroundBrush, "#1D4ED8"),
        (ThemeResourceKeys.FocusBorderBrush, "#60A5FA"),
        (ThemeResourceKeys.SelectedBackgroundBrush, "#1E40AF"),

        (ThemeResourceKeys.FolderRowBackgroundBrush, "#1E293B"),
        (ThemeResourceKeys.FolderRowHoverBackgroundBrush, "#1E3A5F"),
        (ThemeResourceKeys.FolderContainerBorderBrush, "#1E293B"),
        (ThemeResourceKeys.TreeRowHoverBackgroundBrush, "#1E3A5F"),
        (ThemeResourceKeys.DragSourceBackgroundBrush, "#4A3718"),
        (ThemeResourceKeys.DropPlaceholderBackgroundBrush, "#151F2E"),
        (ThemeResourceKeys.DropPlaceholderHoverBackgroundBrush, "#1E40AF"),
        (ThemeResourceKeys.DropPlaceholderBorderBrush, "#94A3B8"),
        (ThemeResourceKeys.GlyphBrush, "#CBD5E1"),

        (ThemeResourceKeys.SearchBoxBackgroundBrush, "#151F2E"),
        (ThemeResourceKeys.SearchBoxBorderBrush, "#475569"),
        (ThemeResourceKeys.SearchIconBrush, "#94A3B8"),
        (ThemeResourceKeys.SearchClearButtonForegroundBrush, "#CBD5E1"),
        (ThemeResourceKeys.SearchEmptyTextBrush, "#94A3B8"),

        (ThemeResourceKeys.UrlTextBrush, "#94A3B8"),
        (ThemeResourceKeys.UrlHoverTextBrush, "#60A5FA"),
        (ThemeResourceKeys.UrlUnderlineBrush, "#60A5FA"),

        (ThemeResourceKeys.ActionOpenBrush, "#60A5FA"),
        (ThemeResourceKeys.ActionOpenHoverBackgroundBrush, "#1E3A5F"),
        (ThemeResourceKeys.ActionAddBookmarkBrush, "#4ADE80"),
        (ThemeResourceKeys.ActionAddBookmarkHoverBackgroundBrush, "#163B25"),
        (ThemeResourceKeys.ActionAddFolderBrush, "#38BDF8"),
        (ThemeResourceKeys.ActionAddFolderHoverBackgroundBrush, "#12364A"),
        (ThemeResourceKeys.ActionEditBrush, "#FACC15"),
        (ThemeResourceKeys.ActionEditHoverBackgroundBrush, "#42370D"),
        (ThemeResourceKeys.ActionDeleteBrush, "#F87171"),
        (ThemeResourceKeys.ActionDeleteHoverBackgroundBrush, "#4A1D1D"),

        (ThemeResourceKeys.DialogBackgroundBrush, "#151F2E"),
        (ThemeResourceKeys.DialogTextPrimaryBrush, "#E5E7EB"),
        (ThemeResourceKeys.DialogTextSecondaryBrush, "#CBD5E1"),
        (ThemeResourceKeys.DialogInputBackgroundBrush, "#111827"),
        (ThemeResourceKeys.DialogInputBorderBrush, "#475569"),
        (ThemeResourceKeys.DialogButtonBackgroundBrush, "#151F2E"),
        (ThemeResourceKeys.DialogButtonHoverBackgroundBrush, "#1E293B"),
        (ThemeResourceKeys.DialogButtonBorderBrush, "#475569"),
        (ThemeResourceKeys.DialogAccentButtonBackgroundBrush, "#2563EB"),
        (ThemeResourceKeys.DialogAccentButtonHoverBackgroundBrush, "#1D4ED8"),
        (ThemeResourceKeys.DialogAccentButtonFocusBackgroundBrush, "#3B82F6"),
        (ThemeResourceKeys.DialogDangerButtonBackgroundBrush, "#DC2626"),
        (ThemeResourceKeys.DialogDangerButtonHoverBackgroundBrush, "#B91C1C"));

    private static ThemePalette Create(params (string Key, string Color)[] colors)
    {
        return new ThemePalette(colors.ToDictionary(
            color => color.Key,
            color => Color.Parse(color.Color)));
    }
}
