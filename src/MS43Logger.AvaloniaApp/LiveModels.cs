using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>One cluster dial on the Live screen.</summary>
public sealed class GaugeViewModel : ViewModelBase
{
    private double? _value;
    private double? _redFrom;
    private string? _channelId;

    public GaugeViewModel(string title, string unit, double min, double max, double major, double minor, double labelDivisor, int decimals, params string[] candidates)
    {
        Title = title;
        Unit = unit;
        Min = min;
        Max = max;
        Major = major;
        Minor = minor;
        LabelDivisor = labelDivisor;
        Decimals = decimals;
        Candidates = candidates;
    }

    public string Title { get; }

    public string Unit { get; }

    public double Min { get; }

    public double Max { get; }

    public double Major { get; }

    public double Minor { get; }

    /// <summary>Scale labels show value / divisor (the rev counter reads in thousands).</summary>
    public double LabelDivisor { get; }

    public int Decimals { get; }

    /// <summary>ADX channel IDs this dial can show, first match wins (MS43X001 names, then stock names).</summary>
    public IReadOnlyList<string> Candidates { get; }

    public string? ChannelId
    {
        get => _channelId;
        set
        {
            if (SetField(ref _channelId, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
            }
        }
    }

    public bool IsAvailable => ChannelId is not null;

    public double? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>Start of the red zone, or null for none.</summary>
    public double? RedFrom
    {
        get => _redFrom;
        set => SetField(ref _redFrom, value);
    }

    public string FormatValue(double value) =>
        Decimals == 0
            ? Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ')
            : value.ToString("F" + Decimals, CultureInfo.InvariantCulture).Replace('-', '−');

    public static IReadOnlyList<GaugeViewModel> CreateCluster() => new[]
    {
        new GaugeViewModel("Engine speed", "× 1000 rpm", 0, 8000, 1000, 250, 1000, 0, "N", "RPM"),
        new GaugeViewModel("Ignition load", "mg/stk", 0, 600, 100, 25, 1, 0, "MAFIGA", "ENGINELOADMMV"),
        new GaugeViewModel("Ignition advance", "°CRK", -30, 50, 10, 2.5, 1, 1, "IGAAVG", "IGNITIONAVG"),
    };

    /// <summary>The rev limiter of an open tune (first gear of <c>id_n_max_max_mt__gear</c>), or null.</summary>
    public static double? RedlineFrom(CalibrationDocument? document)
    {
        if (document?.Xdf.FindByTitle("id_n_max_max_mt__gear") is not XdfTable table || !table.Z.Data.IsSupported || !document.Image.Contains(table.Z.Data))
        {
            return null;
        }

        var value = document.Reader.ReadTable(table).Physical[0, 0];
        return double.IsFinite(value) ? value : null;
    }
}

/// <summary>The last few seconds of one channel, for its sparkline.</summary>
public sealed class SampleHistory
{
    public const int Capacity = 380;   // about 10 s at 38 samples/s

    private readonly double[] _values = new double[Capacity];
    private int _start;

    public int Count { get; private set; }

    public void Add(double value)
    {
        if (Count < Capacity)
        {
            _values[(_start + Count++) % Capacity] = value;
        }
        else
        {
            _values[_start] = value;
            _start = (_start + 1) % Capacity;
        }
    }

    public IEnumerable<double> Values => Enumerable.Range(0, Count).Select(i => _values[(_start + i) % Capacity]);
}
