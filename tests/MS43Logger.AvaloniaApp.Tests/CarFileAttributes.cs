using Xunit.Sdk;

namespace MS43Logger.AvaloniaApp.Tests;

/// <summary>An Avalonia fact that needs the car file and its XDF from testdata/local: skipped, not passed, without them.</summary>
[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaUIFactDiscoverer", "Avalonia.Headless.XUnit")] // as [AvaloniaFact], which is sealed
public sealed class CarAvaloniaFactAttribute : FactAttribute
{
    public CarAvaloniaFactAttribute()
    {
        if (!TuneScreen.Available)
            Skip = "Needs the car file and its XDF in testdata/local";
    }
}

/// <summary>An Avalonia theory that needs the car file and its XDF from testdata/local: skipped, not passed, without them.</summary>
[XunitTestCaseDiscoverer("Avalonia.Headless.XUnit.AvaloniaTheoryDiscoverer", "Avalonia.Headless.XUnit")] // as [AvaloniaTheory], which is sealed
public sealed class CarAvaloniaTheoryAttribute : TheoryAttribute
{
    public CarAvaloniaTheoryAttribute()
    {
        if (!TuneScreen.Available)
            Skip = "Needs the car file and its XDF in testdata/local";
    }
}

/// <summary>A plain fact that needs the car file and its XDF from testdata/local: skipped, not passed, without them.</summary>
public sealed class CarFactAttribute : FactAttribute
{
    public CarFactAttribute()
    {
        if (!TuneScreen.Available)
            Skip = "Needs the car file and its XDF in testdata/local";
    }
}
