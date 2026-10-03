using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core.Faults;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>One stored fault as the dialog lists it.</summary>
public sealed record FaultRow(string PCode, string Name, string Detail);

/// <summary>
/// The ECU's stored faults, read only (DS2 command 06 between the logger's polls). It never clears anything. States: reading,
/// a failed or refused read, no stored fault, or the list oldest first.
/// </summary>
public sealed class FaultMemoryViewModel(Func<IDs2Line?> line) : ViewModelBase
{
    private bool _isReading;
    private string? _problem;
    private IReadOnlyList<FaultRow> _faults = Array.Empty<FaultRow>();
    private bool _hasRead;
    private string? _queues;

    public bool IsReading { get => _isReading; private set { if (SetField(ref _isReading, value)) Changed(); } }

    /// <summary>Why the last read gave nothing; null when it worked.</summary>
    public string? Problem { get => _problem; private set { if (SetField(ref _problem, value)) Changed(); } }

    public IReadOnlyList<FaultRow> Faults { get => _faults; private set { if (SetField(ref _faults, value)) Changed(); } }

    /// <summary>The task queues' overflow flags in words; null until a read has given them.</summary>
    public string? Queues { get => _queues; private set => SetField(ref _queues, value); }

    public bool HasFaults => !IsReading && Problem is null && Faults.Count > 0;

    public bool ShowsNone => !IsReading && _hasRead && Problem is null && Faults.Count == 0;

    public bool CanReadAgain => !IsReading;

    public string Summary => IsReading ? "Reading the fault memory…"
        : Problem is not null ? Problem
        : !_hasRead ? string.Empty
        : Faults.Count == 0 ? "No stored faults."
        : Faults.Count == 1 ? "1 stored fault." : $"{Faults.Count} stored faults, oldest first.";

    public async Task ReadAsync()
    {
        if (IsReading) return;
        if (line() is not { } ds2)
        {
            _hasRead = true;
            (Faults, Problem) = (Array.Empty<FaultRow>(), "Connect to the ECU to read its fault memory.");
            return;
        }

        IsReading = true;
        Queues = null;                                       // a line from the read before must not look current
        try
        {
            var (faults, queues) = await Task.Run(() => (FaultMemory.ReadStored(ds2), FaultMemory.ReadQueueOverflows(ds2)));
            Faults = faults.Select(Row).ToArray();
            Problem = null;
            Queues = QueueText(queues);
        }
        catch (NotSupportedException error)
        {
            (Faults, Problem, Queues) = (Array.Empty<FaultRow>(), error.Message, null);
        }
        catch (Exception error)                              // a timeout, a refusal, a garbled frame, or the link closing mid-read
        {
            (Faults, Problem, Queues) = (Array.Empty<FaultRow>(), $"The fault memory could not be read: {error.Message} Try again.", null);
        }
        finally
        {
            _hasRead = true;
            IsReading = false;
        }
    }

    private static string QueueText(IReadOnlyList<int> queues) => queues.Count switch
    {
        0 => "Task queues since key-on: no overflow.",
        1 => $"Task queues since key-on: queue {queues[0]} overflowed, the ECU ran out of CPU time.",
        _ => $"Task queues since key-on: queues {string.Join(" and ", queues)} overflowed, the ECU ran out of CPU time.",
    };

    internal static FaultRow Row(EcuFault fault)
    {
        var parts = new List<string> { fault.TypeText, fault.Present ? "present now" : "not present now" };
        if (fault.Sporadic) parts.Add("sporadic");
        parts.Add($"frequency count {fault.Frequency}");
        if (fault.Mil) parts.Add("turns on the engine light");
        return new FaultRow(fault.PCode, fault.Label, string.Join(" · ", parts));
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasFaults));
        OnPropertyChanged(nameof(ShowsNone));
        OnPropertyChanged(nameof(CanReadAgain));
        OnPropertyChanged(nameof(Summary));
    }
}
