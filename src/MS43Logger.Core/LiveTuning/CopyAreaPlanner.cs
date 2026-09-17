namespace MS43Logger.Core.LiveTuning;

/// <summary>Places a map around the maps already in the ECU's copy area.</summary>
public static class CopyAreaPlanner
{

    private static int? FirstFit(int bytes, IEnumerable<(int Table, int Start, int End)> taken)
    {
        var at = 0;
        foreach (var (_, start, end) in taken.OrderBy(e => e.Start))
        {
            if (start - at >= bytes)
            {
                return at;
            }

            at = Math.Max(at, end);
        }

        return OverlayContract.CopyBytes - at >= bytes ? at : null;
    }
}
