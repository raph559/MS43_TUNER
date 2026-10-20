using Avalonia.Controls;
using Avalonia.Interactivity;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>Asks whether to save before the open bin closes; shown in the window's <see cref="DialogHost"/>.</summary>
public partial class ConfirmDialog : UserControl
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <param name="message">"Save the changes to CAR.bin?"</param>
    /// <param name="detail">What Save keeps (<see cref="SaveDetail"/>); null hides the line.</param>
    public ConfirmDialog(string message, string? detail)
        : this()
    {
        this.FindControl<TextBlock>("MessageText")!.Text = message;
        var line = this.FindControl<TextBlock>("DetailText")!;
        line.Text = detail;
        line.IsVisible = detail is not null;
    }

    /// <summary>The prompt's second line, from the Saving settings: it never promises a .bak or a log line that Save will not write.</summary>
    public static string? SaveDetail(bool keepBackup, bool appendEditLog) => (keepBackup, appendEditLog) switch
    {
        (true, true) => "Saving keeps a .bak of the file as it was and adds the changes to its edit log.",
        (true, false) => "Saving keeps a .bak of the file as it was.",
        (false, true) => "Saving adds the changes to its edit log.",
        _ => null,
    };

    private void Save_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this, SaveChoice.Save);

    private void DontSave_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this, SaveChoice.DontSave);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this, SaveChoice.Cancel);
}
