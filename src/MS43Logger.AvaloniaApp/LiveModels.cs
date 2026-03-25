using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

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
