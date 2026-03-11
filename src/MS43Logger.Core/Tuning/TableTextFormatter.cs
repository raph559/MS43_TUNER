using System.Globalization;
using System.Text;

namespace MS43Logger.Core.Tuning;

/// <summary>Plain-text rendering of XDF items, for the CLI and for checks against TunerPro.</summary>
public static class TableTextFormatter
{

    private static string Label(AxisValues axis, int index) => index < axis.Labels.Count ? axis.Labels[index] : string.Empty;
}
