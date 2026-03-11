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

public sealed record XdfZ(XdfData Data, XdfMath Math, string Units, int? DecimalPlaces, int? OutputType, double? Min, double? Max);
