using System.IO.Pipes;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public sealed class TuneStateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ms43-tunestate-" + Guid.NewGuid().ToString("N"));

    public TuneStateTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static (MainWindow Window, ShellViewModel Shell) ShowTune()
    {
        var (window, shell) = TestShell.Create();
        shell.SelectedTab = ShellViewModel.TuneTab;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell);
    }

    /// <summary>
    /// Opens the car bin with an XDF read from a pipe this test feeds, so the reading lasts until <paramref name="look"/>
    /// has checked the screen; then feeds the real XDF and lets the open finish.
    /// </summary>
    private static async Task OpenThroughAPipeAsync(ShellViewModel shell, Action<string> look)
    {
        var name = "ms43-xdf-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var opening = shell.Tune.OpenAsync(TuneScreen.Car!, @"\\.\pipe\" + name);
        await pipe.WaitForConnectionAsync();
        Dispatcher.UIThread.RunJobs();

        look(name);

        await using (var xdf = File.OpenRead(TuneScreen.Xdf!))
        {
            await xdf.CopyToAsync(pipe);
        }

        pipe.Disconnect();
        await opening;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void FirstRunOffersOpenBinAndOpenXdf()
    {
        var (window, _) = ShowTune();

        Assert.Equal("Open a bin to start tuning", Find.Named<TextBlock>(window, "EmptyTitle").Text);
        var open = Find.Named<Button>(window, "OpenBinButton");
        Assert.Contains("primary", open.Classes);
        Assert.Equal("Open bin…", open.Content);
        Assert.Equal("Open XDF…", Find.Named<Button>(window, "OpenXdfButton").Content);
        Screens.Save(window, "restyle-tune-night-1440-firstrun");
    }

    [AvaloniaFact]
    public async Task OpeningSaysWhatIsBeingRead()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = TestShell.Create();
        var seen = new List<string>();
        shell.Tune.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TuneViewModel.BusyText) && shell.Tune.BusyText is { } text)
            {
                seen.Add(text);
            }
        };

        await shell.Tune.OpenAsync(TuneScreen.Car!, TuneScreen.Xdf);

        Assert.Equal(new[] { "Reading CAR_current_43X001_SP95_STAGE1.bin…", "Reading Siemens_MS43_MS43X001_512K.xdf…" }, seen);
        Assert.False(shell.Tune.IsBusy);
        Assert.Null(shell.Tune.BusyText);
    }

    [AvaloniaFact]
    public async Task FirstOpenShowsTheLoadingBlock()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = ShowTune();

        await OpenThroughAPipeAsync(shell, name =>
        {
            Assert.Equal($"Reading {name}…", Find.Named<TextBlock>(window, "EmptyBusyText").Text);
            Assert.True(Find.Named<ProgressBar>(window, "EmptyBusyBar").IsEffectivelyVisible);
            Assert.False(Find.Named<Button>(window, "OpenBinButton").IsEffectivelyVisible);
            Screens.Save(window, "restyle-tune-night-1440-loading");
        });

        Assert.NotNull(shell.Tune.Document);
        Assert.False(Find.Named<ProgressBar>(window, "EmptyBusyBar").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task OpeningAnotherKeepsTheOpenBinAndRunsTheBar()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();

        await OpenThroughAPipeAsync(shell, _ =>
        {
            Assert.True(Find.Named<ProgressBar>(window, "BusyBar").IsEffectivelyVisible);
            Assert.True(Find.Named<Control>(window, "RailList").IsEffectivelyVisible);
            Assert.False(Find.Named<Control>(window, "TunePane").IsEffectivelyEnabled);
        });

        Assert.False(Find.Named<ProgressBar>(window, "BusyBar").IsEffectivelyVisible);
        Assert.True(Find.Named<Control>(window, "TunePane").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task UnreadableXdfSaysNotOpened()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var broken = Path.Combine(_folder, "broken.xdf");
        File.WriteAllText(broken, "<XDFFORMAT><XDFHEADER>");
        var (_, shell) = TestShell.Create();

        await shell.Tune.OpenAsync(TuneScreen.Car!, broken);

        Assert.Null(shell.Tune.Document);
        Assert.StartsWith("Not opened: broken.xdf could not be read (", shell.Tune.ErrorMessage);
        Assert.EndsWith(").", shell.Tune.ErrorMessage);
        Assert.Equal(TuneAction.OpenXdf, shell.Tune.ErrorAction);
    }

    [AvaloniaFact]
    public async Task WrongSizeBinShowsABannerWithOpenBin()
    {
        var partial = Path.Combine(_folder, "backup.bin");
        File.WriteAllBytes(partial, new byte[262144]);
        var (window, shell) = ShowTune();

        await shell.Tune.OpenAsync(partial, TuneScreen.Xdf ?? partial);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Not opened: backup.bin is 262144 bytes. An MS43 bin is exactly 524288 bytes (512 KiB).", Find.Named<TextBlock>(window, "EditError").Text);
        Assert.True(Find.Named<TextBlock>(window, "EditError").IsEffectivelyVisible);
        Assert.Equal("Open bin…", Find.Named<Button>(window, "ErrorActionButton").Content);
        Assert.True(Find.Named<Button>(window, "OpenBinButton").IsEffectivelyVisible);
        Screens.Save(window, "restyle-tune-night-1440-wrongsize");
    }

    [AvaloniaFact]
    public async Task XdfNeededSaysSoInTheEmptyBlock()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = ShowTune();

        await shell.Tune.OpenAsync(TuneScreen.Car!, xdfPath: null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Choose the XDF for firmware 43X001.", Find.Named<TextBlock>(window, "XdfNeededText").Text);
        Assert.True(Find.Named<TextBlock>(window, "XdfNeededText").IsEffectivelyVisible);
        Assert.Contains("primary", Find.Named<Button>(window, "OpenXdfButton").Classes);
        Assert.DoesNotContain("primary", Find.Named<Button>(window, "OpenBinButton").Classes);
        Assert.False(Find.Named<Control>(window, "ErrorBanner").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task FirmwareMismatchOffersAnotherXdf()
    {
        var stock = TuneRailTests.Local("MS43_430069_stock.bin");
        if (stock is null || TuneScreen.Xdf is null)
        {
            return;
        }

        var (window, shell) = ShowTune();

        await shell.Tune.OpenAsync(stock, TuneScreen.Xdf);
        Dispatcher.UIThread.RunJobs();

        var banner = Find.Named<Control>(window, "FirmwareBanner");
        Assert.True(banner.IsEffectivelyVisible);
        Assert.Contains(banner.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Use another XDF…"));
    }

    [AvaloniaFact]
    public async Task UnexpectedErrorWhileOpeningIsShownNotThrown()
    {
        var (_, shell) = TestShell.Create();

        await shell.Tune.OpenAsync("bad\0name.bin", null);

        Assert.Null(shell.Tune.Document);
        Assert.StartsWith("Not opened: ", shell.Tune.ErrorMessage);
        Assert.False(shell.Tune.IsBusy);
    }

    [AvaloniaFact]
    public async Task ReadOnlyItemShowsChip()
    {
        const string xml = """
            <XDFFORMAT><XDFHEADER><deftitle>TEST</deftitle><BASEOFFSET offset="458752" subtract="0" /></XDFHEADER>
              <XDFTABLE uniqueid="0x10"><title>colmajor</title>
                <XDFAXIS id="z"><EMBEDDEDDATA mmedtypeflags="0x06" mmedaddress="0x7500" mmedelementsizebits="8" mmedrowcount="2" />
                <MATH equation="X"><VAR id="X" /></MATH></XDFAXIS></XDFTABLE>
            </XDFFORMAT>
            """;
        var xdf = Path.Combine(_folder, "test.xdf");
        var bin = Path.Combine(_folder, "zero.bin");
        File.WriteAllText(xdf, xml, Encoding.UTF8);
        File.WriteAllBytes(bin, new byte[BinImage.Size]);
        var (window, shell) = ShowTune();

        await shell.Tune.OpenAsync(bin, xdf);
        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("colmajor");
        Dispatcher.UIThread.RunJobs();

        Assert.True(Find.Named<Control>(window, "ReadOnlyChip").IsEffectivelyVisible);
        var tools = Find.Named<Control>(window, "ToolBar");
        Assert.False(tools.IsEffectivelyEnabled);
        Assert.False(Find.Named<TextBox>(window, "ToolValue").IsEffectivelyEnabled);
        Assert.StartsWith("'colmajor' is read-only: ", ToolTip.GetTip(tools) as string);
    }

    // Saved, but the edit log was locked (open in Excel): the message must say the bin was saved.
    [AvaloniaFact]
    public async Task SaveWithALockedEditLogSaysItSaved()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var bin = TuneScreen.CopyCar(_folder);
        var (_, shell) = await TuneScreen.OpenAsync(binPath: bin);
        shell.Tune.Editor!.Selection = new CellRange(0, 0, 0, 0);
        shell.Tune.Editor.Apply(new EditRequest(EditKind.Step, 1));

        using (File.Open(EditLog.PathFor(bin), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(shell.Tune.Save());
        }

        Assert.StartsWith("Saved. The edit log CAR.log was not written:", shell.Tune.ErrorMessage);
        Assert.Equal("Saved", shell.Tune.SaveStateText);
        Assert.False(shell.Tune.Document!.IsModified);
    }
}
