namespace MS43Logger.Core.LiveTuning;

/// <summary>Where the screens set the V9 watch: the map whose data address the ECU should record.</summary>
public interface IWatchTarget
{
    /// <summary>Asks for the watch on <paramref name="address"/> (a map's data address), or none for null.</summary>
    void SetWatch(uint? address);

    /// <summary>The address the ECU has acknowledged; null while none is.</summary>
    uint? Watched { get; }
}
