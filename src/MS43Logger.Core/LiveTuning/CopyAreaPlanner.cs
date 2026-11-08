namespace MS43Logger.Core.LiveTuning;

/// <summary>Places a map around the maps already in the ECU's copy area.</summary>
public static class CopyAreaPlanner
{
    /// <summary>The map's offset: its own if it is already in the area, else the first gap that holds it; null when none does.</summary>
    public static int? Place(int table, OverlayStatus status)
    {
        if (status.Version is OverlayV5.Version or OverlayV6.Version or OverlayV7.Version or OverlayV8.Version or OverlayV9.Version or OverlayV10.Version) return OverlayV5.Place(table, status)?.Offset;
        if (status.Version == OverlayV4.Version) return OverlayV4.Place(table, status);
        if (status.Version == OverlayV3.Version) return OverlayV3.Offset(table);
        if (status.Tables[table].State != OverlayState.Free)
        {
            return status.Tables[table].Offset;
        }

        return FirstFit(OverlayContract.Maps[table].Bytes, Extents(status, except: table));
    }

    /// <summary>The smallest single map (the lowest index on a tie) whose freeing would let <paramref name="table"/> fit; null when none would.</summary>
    public static int? FreeFirst(int table, OverlayStatus status) => status.Version is OverlayV5.Version or OverlayV6.Version or OverlayV7.Version or OverlayV8.Version or OverlayV9.Version or OverlayV10.Version ? OverlayV5.FreeFirst(table, status) :
        status.Version == OverlayV4.Version ? OverlayV4.FreeFirst(table, status) :
        OverlayContract.Maps
            .Where(m => m.Index != table && status.Tables[m.Index].State != OverlayState.Free)
            .OrderBy(m => m.Bytes).ThenBy(m => m.Index)
            .Where(m => FirstFit(OverlayContract.Maps[table].Bytes, Extents(status, except: table).Where(e => e.Table != m.Index)) is not null)
            .Select(m => (int?)m.Index)
            .FirstOrDefault();

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
