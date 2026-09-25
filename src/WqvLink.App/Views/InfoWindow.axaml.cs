using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WqvLink.App.Views;

/// <summary>A simple text window, used for help pages, messages and confirmations.</summary>
public sealed partial class InfoWindow : Window
{
    public InfoWindow()
    {
        InitializeComponent();
    }

    public InfoWindow(string title, string text, bool monospace = false, string? confirmText = null) : this()
    {
        Title = title;
        Body.Text = text;
        if (monospace)
        {
            Body.FontFamily = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,monospace");
            Body.TextWrapping = TextWrapping.NoWrap;
        }
        if (confirmText is not null)
        {
            OkButton.Content = confirmText;
            CancelButton.IsVisible = true;
        }
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}

/// <summary>Message and confirmation dialogs.</summary>
public static class Dialogs
{
    public static Task InfoAsync(Window owner, string title, string text) =>
        new InfoWindow(title, text).ShowDialog(owner);

    public static Task<bool> ConfirmAsync(Window owner, string title, string text, string confirmText) =>
        new InfoWindow(title, text, confirmText: confirmText).ShowDialog<bool>(owner);
}
