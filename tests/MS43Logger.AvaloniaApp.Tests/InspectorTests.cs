using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MS43Logger.AvaloniaApp.Controls;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp.Tests;

public class InspectorTests
{
    [AvaloniaFact]
    public void ProjectionKeepsCentreFixed()
    {
        var size = new Size(300, 250);
        foreach (var (yaw, pitch) in new[] { (-0.72, 0.6), (0.4, 1.2), (2.0, 0.3) })
        {
            var (point, _) = SurfaceMath.Project(0.5, 0.5, 0, yaw, pitch, size);
            Assert.Equal(150, point.X, 6);
            Assert.Equal(250 * 0.58, point.Y, 6);
        }
    }

    [AvaloniaFact]
    public async Task MapShowsSurfaceCurveShowsChart()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync();
        Assert.True(Find.Named<Surface3D>(window, "Surface").IsEffectivelyVisible);
        Assert.False(Find.Named<CurveChart>(window, "Curve").IsEffectivelyVisible);
        Screens.Save(window, "tune-map-inspector");

        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("id_n_max_max_mt__gear");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(Find.Named<Surface3D>(window, "Surface").IsEffectivelyVisible);
        Assert.True(Find.Named<CurveChart>(window, "Curve").IsEffectivelyVisible);
        Screens.Save(window, "tune-curve");
    }

    [AvaloniaFact]
    public async Task SwitchShowsItsChoices()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: "c_conf_load");
        var value = shell.Tune.Value!;

        Assert.Equal(new[] { "0: Map sensor", "1: Maf sensor", "2: Alpha-N" }, value.Choices!.Select(choice => choice.Text));
        Assert.Equal(1, value.SelectedChoice!.Value);
        Assert.True(Find.Named<ComboBox>(window, "ChoiceBox").IsEffectivelyVisible);
        Screens.Save(window, "tune-switch");
    }

    [AvaloniaFact]
    public async Task ChangingSwitchGoesThroughHistory()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (_, shell) = await TuneScreen.OpenAsync(select: "c_conf_load");
        var value = shell.Tune.Value!;

        value.SelectedChoice = value.Choices![0];

        Assert.Equal(0, value.Current.Raw);
        Assert.Equal("1 unsaved change", shell.Tune.DirtyText);
        shell.Tune.History!.Undo();
        Assert.Equal(1, shell.Tune.Value!.Current.Raw);
        Assert.Equal(1, shell.Tune.Value.SelectedChoice!.Value);
    }

    [AvaloniaFact]
    public async Task ValuesAndFlagsEdit()
    {
        if (!TuneScreen.Available)
        {
            return;
        }

        var (window, shell) = await TuneScreen.OpenAsync(select: "c_lc_n_max");
        var value = shell.Tune.Value!;
        Assert.Equal("4512", value.Current.Display);
        Screens.Save(window, "tune-value");

        value.Input = "4000";
        Assert.True(value.Apply());
        Assert.Equal(125, value.Current.Raw);

        shell.Tune.SelectedItem = shell.Tune.Document!.Xdf.FindByTitle("lv_mil_ind_knk_lv_1");
        var flag = shell.Tune.Value!;
        Assert.False(flag.IsOn);
        flag.IsOn = true;
        Assert.True(shell.Tune.Document.Reader.ReadFlag((XdfFlag)shell.Tune.SelectedItem!));
    }
}
