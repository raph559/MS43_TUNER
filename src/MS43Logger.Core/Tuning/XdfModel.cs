namespace MS43Logger.Core.Tuning;

/// <summary>Where and how an XDF item's elements are stored. <see cref="Address"/> already includes the XDF base offset.</summary>
public sealed record XdfData(long? Address, int ElementBits, bool Signed, bool LsbFirst, int Rows, int Cols, string? UnsupportedReason)
{
    public int Count => Rows * Cols;

    public int ElementBytes => ElementBits / 8;

    public bool IsSupported => UnsupportedReason is null;
}

public enum XdfVariableKind
{
    Raw,
    Link,
    Unsupported,
}

public sealed record XdfVariable(string Id, XdfVariableKind Kind, int? LinkId);

public sealed record XdfMath(string Equation, IReadOnlyList<XdfVariable> Variables);

public sealed record XdfAxis(
    string Id,
    string Units,
    int Count,
    XdfData? Data,
    int? LinkedTableId,
    IReadOnlyList<string> Labels,
    XdfMath Math,
    int? DecimalPlaces);

public sealed record XdfZ(XdfData Data, XdfMath Math, string Units, int? DecimalPlaces, int? OutputType, double? Min, double? Max);

public abstract record XdfItem(int UniqueId, string Title, string Description, IReadOnlyList<int> CategoryIds);

public sealed record XdfTable(int UniqueId, string Title, string Description, IReadOnlyList<int> CategoryIds, XdfAxis X, XdfAxis Y, XdfZ Z)
    : XdfItem(UniqueId, Title, Description, CategoryIds);

public sealed record XdfConstant(
    int UniqueId,
    string Title,
    string Description,
    IReadOnlyList<int> CategoryIds,
    XdfData Data,
    XdfMath Math,
    string Units,
    int? DecimalPlaces,
    int? OutputType,
    double? RangeLow,
    double? RangeHigh)
    : XdfItem(UniqueId, Title, Description, CategoryIds);

public sealed record XdfFlag(int UniqueId, string Title, string Description, IReadOnlyList<int> CategoryIds, XdfData Data, long Mask)
    : XdfItem(UniqueId, Title, Description, CategoryIds);
