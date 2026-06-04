using System;
using System.Collections.Generic;
using System.Linq;
using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// One reading of Live's band (system 8): the first of its candidate channels the definition has, that channel's ADX
/// title and unit, and the value of the last sample, null when the sample did not hold it.
/// </summary>
public class ReadoutViewModel : ViewModelBase
{
    private double? _value;
    private string? _channelId;
    private string _channelTitle;
    private string _unit;

    public ReadoutViewModel(string title, string unit, int decimals, params string[] candidates)
    {
        Title = title;
        _channelTitle = title;
        _unit = unit;
        Decimals = decimals;
        Candidates = candidates;
    }

    /// <summary>The name used until a definition names the channel.</summary>
    public string Title { get; }

    /// <summary>The band's rounding (system 3.3): rpm and speed whole, load and advance one decimal.</summary>
    public int Decimals { get; }

    /// <summary>ADX channel IDs this reading can show, first match wins (MS43X001 names, then stock names).</summary>
    public IReadOnlyList<string> Candidates { get; }

    public string? ChannelId
    {
        get => _channelId;
        set
        {
            if (SetField(ref _channelId, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(FullName));
            }
        }
    }

    public bool IsAvailable => ChannelId is not null;

    /// <summary>The channel's ADX title, as the channel list names it ("Engine Load Ignition").</summary>
    public string ChannelTitle
    {
        get => _channelTitle;
        private set
        {
            if (SetField(ref _channelTitle, value))
            {
                OnPropertyChanged(nameof(FullName));
            }
        }
    }

    /// <summary>"Engine Load Ignition MAFIGA": the cell's tooltip, since narrow cells trim their name.</summary>
    public string FullName => ChannelId is null ? ChannelTitle : $"{ChannelTitle} {ChannelId}";

    /// <summary>The reading's unit, the channel's own where the definition gives one ("mg/stk").</summary>
    public string Unit { get => _unit; private set => SetField(ref _unit, value); }

    public double? Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                OnPropertyChanged(nameof(Display));
                OnPropertyChanged(nameof(HasValue));
            }
        }
    }

    public bool HasValue => Value is double value && double.IsFinite(value);

    /// <summary>"2480", "112.4", or "n/a" when the last sample did not hold the channel.</summary>
    public string Display => NumberFormat.Format(Value, Decimals);

    /// <summary>Picks the channel from a definition's channels and takes its title and unit.</summary>
    public void Resolve(IReadOnlyList<AdxValueDefinition> channels, Func<string, string> units)
    {
        var channel = Candidates.Select(id => channels.FirstOrDefault(candidate => candidate.Id == id)).FirstOrDefault(found => found is not null);
        ChannelId = channel?.Id;
        ChannelTitle = channel?.DisplayName is { Length: > 0 } title ? title : Title;
        if (channel is not null && units(channel.Units) is { Length: > 0 } unit)
        {
            Unit = unit;
        }
    }
}

/// <summary>One readout of the band with a scale: a bar in Tiles, a dial in Dials (system 8).</summary>
public sealed class GaugeViewModel : ReadoutViewModel
{
    private double? _value;
    private double? _redFrom;

    public GaugeViewModel(string title, string unit, double min, double max, double major, double minor, double labelDivisor, int decimals, params string[] candidates)
        : base(title, unit, decimals, candidates)
    {
        Min = min;
        Max = max;
        Major = major;
        Minor = minor;
        LabelDivisor = labelDivisor;
    }

    public double Min { get; }

    public double Max { get; }

    /// <summary>Labelled ticks, from <see cref="Min"/> on: 0, 2000 … 8000 rpm.</summary>
    public double Major { get; }

    public double Minor { get; }

    /// <summary>Dial labels show value / divisor (the rev counter reads in thousands, "× 1000 rpm").</summary>
    public double LabelDivisor { get; }

    public double? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>The dial's note under its top label, "× 1000 rpm"; null when the labels are plain values.</summary>
    public string? ScaleNote { get; init; }

    /// <summary>A scale that crosses zero fills from zero and marks it (advance).</summary>
    public double? ZeroAt { get; init; }

    /// <summary>Start of the rev-limit band, or null for none.</summary>
    public double? RedFrom
    {
        get => _redFrom;
        set => SetField(ref _redFrom, value);
    }

    public static IReadOnlyList<GaugeViewModel> CreateCluster() => new[]
    {
        new GaugeViewModel("Engine Speed", "rpm", 0, 8000, 2000, 1000, 1000, 0, "N", "RPM") { ScaleNote = "× 1000 rpm" },
        new GaugeViewModel("Engine Load Ignition", "mg/stk", 0, 600, 300, 100, 1, 1, "MAFIGA", "ENGINELOADMMV"),
        new GaugeViewModel("Ignition Angle Average", "°CRK", -30, 50, 80, 10, 1, 1, "IGAAVG", "IGNITIONAVG") { ZeroAt = 0 },
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

/// <summary>
/// One channel's samples over the trend window (system 5.6), by the time each arrived, for its sparkline. It holds up to
/// 64 samples a second of the window; older ones drop out first.
/// </summary>
public sealed class SampleHistory
{
    public const int PerSecond = 64;

    private readonly (DateTimeOffset Time, double Value)[] _points;
    private int _start;

    public SampleHistory(TimeSpan window)
    {
        Span = window;
        _points = new (DateTimeOffset, double)[Math.Max(2, (int)Math.Ceiling(window.TotalSeconds * PerSecond))];
    }

    /// <summary>How far back the window reaches: the Trend length setting.</summary>
    public TimeSpan Span { get; }

    public int Count { get; private set; }

    /// <summary>The newest sample's time, the right end of the sparkline.</summary>
    public DateTimeOffset? Latest => Count == 0 ? null : _points[(_start + Count - 1) % _points.Length].Time;

    public void Add(DateTimeOffset time, double value)
    {
        if (Count < _points.Length)
        {
            _points[(_start + Count++) % _points.Length] = (time, value);
        }
        else
        {
            _points[_start] = (time, value);
            _start = (_start + 1) % _points.Length;
        }
    }

    public void Clear()
    {
        _start = 0;
        Count = 0;
    }

    /// <summary>Every sample held, oldest first.</summary>
    public IEnumerable<(DateTimeOffset Time, double Value)> Points => Enumerable.Range(0, Count).Select(i => _points[(_start + i) % _points.Length]);

    /// <summary>The samples from <paramref name="now"/> less the window up to <paramref name="now"/>, oldest first.</summary>
    public IReadOnlyList<(DateTimeOffset Time, double Value)> Window(DateTimeOffset now)
    {
        var from = now - Span;
        return Points.Where(point => point.Time >= from && point.Time <= now).ToList();
    }
}
