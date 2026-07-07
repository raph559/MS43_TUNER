using System.Text.Json;
using System.Text.Json.Serialization;

namespace MS43Logger.Core.Performance;

/// <summary>
/// A saved run as it is stored, one JSON file per result: who and what (name, description, direction),
/// the test and the inputs its figures were computed with, the tune, the conditions, the figures, and every sample of the
/// window plus the 2 s before it by column, so <see cref="RunAnalysis.Analyse"/> can recompute it later. Name, description
/// and direction are the only parts the user edits.
/// </summary>
public sealed class PerfResult
{
    /// <summary>1 in this release; a file with a higher schema is listed read-only and never rewritten. A file without one is no result.</summary>
    [JsonPropertyOrder(-1)]
    [JsonRequired]
    public int Schema { get; init; } = 1;

    /// <summary>A Guid in its 32-digit "N" form.</summary>
    public string Id { get; init; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>"A", "B" or null: which way the road was driven, so slope can be told apart.</summary>
    public string? Direction { get; set; }

    public DateTimeOffset Created { get; init; }

    public string AppVersion { get; init; } = "";

    public PerfTestSpec Spec { get; init; } = default!;

    public bool Complete { get; init; }

    public RunFigures Figures { get; init; } = default!;

    public Conditions Start { get; init; } = default!;

    public Conditions End { get; init; } = default!;

    /// <summary>When the ready checks were met; null in a result saved before it was kept.</summary>
    public Conditions? Armed { get; init; }

    /// <summary>The car values, speed calibration and full-pedal value <see cref="Figures"/> were computed with.</summary>
    public AnalysisInputs Inputs { get; init; } = default!;

    /// <summary>The bundled definition the payloads decode with: "ms43x", "b25", ….</summary>
    public string Variant { get; init; } = "";

    public string DefinitionFile { get; init; } = "";

    public string DefinitionSha256 { get; init; } = "";

    /// <summary>The wall time of the first sample.</summary>
    public DateTimeOffset SamplesStart { get; init; }

    /// <summary>Each sample's request time in seconds on the run clock.</summary>
    public double[] SampleAt { get; init; } = [];

    /// <summary>Each sample's raw payload, base64.</summary>
    public string[] Payloads { get; init; } = [];

    /// <summary>The retries each sample's poll needed, one per sample; null when the source reported none.</summary>
    public int[]? SampleRetries { get; init; }

    /// <summary>The first sample in the band (the start crossing lies between it and the one before).</summary>
    public int WindowStart { get; init; }

    /// <summary>The last sample of the run.</summary>
    public int WindowEnd { get; init; }

    /// <summary>Fields a later version of the app wrote that this one does not know, written back as they were.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>What the results list shows of one file; <see cref="ReadOnly"/> marks a newer schema, which is never rewritten.</summary>
public sealed record PerfResultHeader(string FileName, string Id, string Name, string Description, DateTimeOffset Created,
    PerfTestKind Kind, string Preset, string? BinName, string? SavedSha256, string Headline, IReadOnlyList<RunFlag> Flags,
    bool Complete, bool ReadOnly);
