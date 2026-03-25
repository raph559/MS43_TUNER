using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public enum EditKind
{
    Set,
    Add,
    Scale,
    Interpolate,
    Smooth,
    Step,
    Paste,
}

public sealed record EditRequest(EditKind Kind, double Value = 0, string? Text = null);
