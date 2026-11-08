using Avalonia.Controls;
using Avalonia.Interactivity;
using MS43Logger.AvaloniaApp.Shell;

namespace MS43Logger.AvaloniaApp.Views;

/// <summary>The stored faults behind the Live tab's "Fault memory", shown in the window's <see cref="DialogHost"/>; it reads on open.</summary>
public partial class FaultMemoryView : UserControl
{
    public FaultMemoryView()
    {
        InitializeComponent();
    }

    public FaultMemoryView(FaultMemoryViewModel faults)
        : this()
    {
        DataContext = faults;
        _ = faults.ReadAsync();
    }

    private void ReadAgain_Click(object? sender, RoutedEventArgs e) => _ = (DataContext as FaultMemoryViewModel)?.ReadAsync();

    private void Close_Click(object? sender, RoutedEventArgs e) => DialogHost.CloseFrom(this);
}
