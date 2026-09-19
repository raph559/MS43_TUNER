namespace MS43Logger.Core.LiveTuning;

/// <summary>
/// What detection found: the header, the supported maps' bytes as the ECU's flash holds them, and the
/// problem when the overlay is not there ("not found") or is not the one this app knows ("not understood").
/// </summary>
public sealed record OverlayDetection(OverlayHeader? Header, IReadOnlyList<byte[]> Flash, string? Problem,
    IReadOnlyList<OverlayMap>? Catalogue = null, LimiterSettings? LimiterFlash = null);

/// <summary>A send's outcome: null and null when the map is live; else the refusal, or the map to free first when it does not fit.</summary>
public sealed record SendResult(OverlayReason? Refused, int? FreeFirst);

/// <summary>
/// Talks to the live-tuning overlay over a DS2 line. Detection reads only (DS2 command 06); every other call sends 0x6D, so
/// it is for an ECU whose detection found the overlay.
/// </summary>
public sealed class OverlayClient(IDs2Line line)
{
    private readonly IDs2Line _line = line ?? throw new ArgumentNullException(nameof(line));
    private IReadOnlyList<byte[]>? _flash;
    private OverlayV3Client? _v3;
    private OverlayV4Client? _v4;
    private int _version;
    private int _minor;
    private IReadOnlyList<OverlayMap> _maps = OverlayContract.Maps;

    public OverlayReason? SendLimiter(LimiterSettings settings)
    {
        RequireSupported();
        return (_v4 ?? throw new InvalidOperationException("Limiter editing requires overlay 4, 5 or 6.")).SendLimiter(settings);
    }

    private void RequireSupported()
    {
        if (_flash is null)
        {
            throw new InvalidOperationException($"A matching version {OverlayContract.SupportedVersion} overlay must be detected before sending live commands.");
        }
    }
}
