using System;
using Avalonia.Controls;

namespace Stranichnik.Views;

public sealed partial class StatusBanner : UserControl
{
    public StatusBanner()
    {
        InitializeComponent();
    }

    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        ErrorTextBlock.Text = message;
        ErrorBorder.IsVisible = true;
        SuccessBorder.IsVisible = false;
        InfoBorder.IsVisible = false;
        IsVisible = true;
    }

    public void ShowSuccess(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        SuccessTextBlock.Text = message;
        SuccessBorder.IsVisible = true;
        ErrorBorder.IsVisible = false;
        InfoBorder.IsVisible = false;
        IsVisible = true;
    }

    public void ShowInfo(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        InfoTextBlock.Text = message;
        InfoBorder.IsVisible = true;
        ErrorBorder.IsVisible = false;
        SuccessBorder.IsVisible = false;
        IsVisible = true;
    }

    public void Hide()
    {
        ErrorBorder.IsVisible = false;
        SuccessBorder.IsVisible = false;
        InfoBorder.IsVisible = false;
        IsVisible = false;
    }

    public bool IsShowingInfo()
    {
        return IsVisible && InfoBorder.IsVisible;
    }

    public bool IsShowingMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return IsVisible &&
            ((ErrorBorder.IsVisible &&
                string.Equals(ErrorTextBlock.Text, message, StringComparison.Ordinal)) ||
             (SuccessBorder.IsVisible &&
                string.Equals(SuccessTextBlock.Text, message, StringComparison.Ordinal)) ||
             (InfoBorder.IsVisible &&
                string.Equals(InfoTextBlock.Text, message, StringComparison.Ordinal)));
    }
}
