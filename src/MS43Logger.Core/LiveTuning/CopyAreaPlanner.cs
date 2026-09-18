namespace MS43Logger.Core.LiveTuning;

/// <summary>Places a map around the maps already in the ECU's copy area.</summary>
public static class CopyAreaPlanner
{

    private static IEnumerable<(int Table, int Start, int End)> Extents(OverlayStatus status, int except) =>
        OverlayContract.Maps
            .Where(m => m.Index != except && status.Tables[m.Index].State != OverlayState.Free)
            .Select(m => (m.Index, status.Tables[m.Index].Offset, status.Tables[m.Index].Offset + m.Bytes));

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
