using Avalonia.Controls;
using Avalonia.Interactivity;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>A yes-or-cancel question before an action (swapping A and B on Compare); the dialog's result is true for the action.</summary>
public partial class ConfirmActionDialog : UserControl
{
    public ConfirmActionDialog()
    {
        InitializeComponent();
    }

    /// <param name="message">The question: "Swap A and B? …".</param>
    /// <param name="detail">What follows from it; null hides the line.</param>
    /// <param name="action">The primary button's label, the action's own verb ("Swap").</param>
    public ConfirmActionDialog(string message, string? detail, string action)
        : this()
    {
        MessageText.Text = message;
        DetailText.Text = detail;
        DetailText.IsVisible = detail is not null;
        ConfirmActionButton.Content = action;
    }

    private void Action_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this, true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this, false);
}
