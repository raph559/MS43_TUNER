namespace MS43Logger.Core.LiveTuning;

/// <summary>What the V9 overlay last recorded of the watched map: indices for the readers, inputs for the descriptor lookups.</summary>
public enum WatchKind { None, MapIndices, CurveIndex, MapInputs, CurveInput }

/// <summary>
/// The watch record of a V9 log reply. Indices: Row/Column are the library's indices and the fractions its 16-bit
/// interpolation positions. Inputs: Row/Column are the lookup's raw inputs (R13/R14). Count wraps at 256.
/// </summary>
public sealed record WatchRecord(WatchKind Kind, byte Count, ushort Row, ushort RowFraction, ushort Column, ushort ColumnFraction, byte Columns);
