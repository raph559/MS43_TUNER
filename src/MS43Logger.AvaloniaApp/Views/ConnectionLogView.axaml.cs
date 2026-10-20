using Avalonia.Controls;
using Avalonia.Interactivity;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>The connection log behind a Live banner's "Show details", shown in the window's <see cref="DialogHost"/>.</summary>
public partial class ConnectionLogView : UserControl
{
    public ConnectionLogView()
    {
        InitializeComponent();
    }

    /// <param name="reason">The banner's sentence; null or empty hides the line.</param>
    /// <param name="log">The error in full, stack trace included.</param>
    public ConnectionLogView(string? reason, string log)
        : this()
    {
        var line = this.FindControl<TextBlock>("ReasonText")!;
        line.Text = reason;
        line.IsVisible = !string.IsNullOrEmpty(reason);
        this.FindControl<TextBox>("LogText")!.Text = log;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this);
}
