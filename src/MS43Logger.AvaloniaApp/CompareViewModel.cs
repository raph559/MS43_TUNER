using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public abstract record CompareEntry;

public sealed record CompareGroup(string Title) : CompareEntry;

/// <summary>One differing item in the list.</summary>
public sealed record CompareRow(ItemDifference Difference, bool IsCurrent) : CompareEntry
{
    public string Title => Difference.Item.Title;

    /// <summary>"12 of 320" for tables, "4512 → 3520" for values, switches and flags.</summary>
    public string Summary => Difference.Item is XdfTable
        ? $"{Difference.ChangedCells} of {Difference.Cells}"
        : $"{Difference.ValueA} → {Difference.ValueB}";

    public string Detail => $"{Difference.Category}, {Difference.Kind.ToString().ToLowerInvariant()}";

    public string Units => CompareViewModel.UnitsOf(Difference.Item);
}

/// <summary>The list entry that opens the bytes that differ outside every XDF item.</summary>
public sealed record CompareBytesRow(string Summary, bool IsCurrent) : CompareEntry;

public sealed record KindChip(string Key, string Label, bool IsOn);

/// <summary>The Compare screen: the open bin (A) against a second bin (B) through A's XDF.</summary>
public sealed class CompareViewModel : ViewModelBase
{
    private static readonly (string Key, string Name, ItemKind[] Kinds)[] KindFilters =
    [
        ("all", "All", Enum.GetValues<ItemKind>()),
        ("map", "Maps", [ItemKind.Map]),
        ("curve", "Curves", [ItemKind.Curve]),
        ("axis", "Axes", [ItemKind.Axis]),
        ("value", "Values", [ItemKind.Value, ItemKind.Switch, ItemKind.Flag]),
    ];

    private readonly TuneViewModel _tune;
    private CalibrationDocument? _watched;
    private BinImage? _b;
    private string? _fileB;
    private BinComparison? _result;
    private TableComparison? _table;
    private ItemDifference? _selected;
    private bool _showsBytes;
    private string _kind = "all";
    private string _view = "delta";
    private TableEditorViewModel? _grid;
    private IReadOnlyList<KindChip> _chips = Array.Empty<KindChip>();
    private string? _firmwareWarning;
    private string? _errorMessage;
    private string? _message;
    private bool _stale = true;
    private bool _isVisible;

    public CompareViewModel(TuneViewModel tune)
    {
        _tune = tune ?? throw new ArgumentNullException(nameof(tune));
        tune.DocumentOpened += (_, _) =>
        {
            Watch(tune.Document);
            OnPropertyChanged(nameof(FileA));
            OnPropertyChanged(nameof(HasA));
            Invalidate();
        };
        Watch(tune.Document);
        tune.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TuneViewModel.DiffersOnly) && tune.DiffersOnly && _stale)
            {
                Refresh();
            }
        };
    }

    /// <summary>Asks the shell to show an item on the Tune tab.</summary>
    public event EventHandler<XdfItem>? ShowInTuneRequested;

    public ObservableCollection<CompareEntry> Entries { get; } = new();

    public bool HasA => _tune.Document is not null;

    public bool HasB => _b is not null;

    public bool NeedsB => HasA && !HasB;

    public string FileA => _tune.Document?.Path is { } path ? Path.GetFileName(path) : "No bin open";

    public string? FileB { get => _fileB; private set => SetField(ref _fileB, value); }

    /// <summary>Set when B is other firmware than A.</summary>
    public string? FirmwareWarning { get => _firmwareWarning; private set => SetField(ref _firmwareWarning, value); }

    public string? ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }

    public string? Message { get => _message; private set => SetField(ref _message, value); }

    public BinComparison? Result
    {
        get => _result;
        private set
        {
            if (SetField(ref _result, value))
            {
                OnPropertyChanged(nameof(IsIdentical));
                OnPropertyChanged(nameof(HasDifferences));
                OnPropertyChanged(nameof(UnmappedSummary));
                OnPropertyChanged(nameof(Unmapped));
            }
        }
    }

    public bool IsIdentical => Result is { IsEmpty: true };

    public bool HasDifferences => Result is { IsEmpty: false };

    public IReadOnlyList<UnmappedRange> Unmapped => Result?.Unmapped ?? Array.Empty<UnmappedRange>();

    /// <summary>"18 ranges, 11,907 bytes outside the XDF (11,436 differ)": ranges may span a few equal bytes.</summary>
    public string UnmappedSummary => Result is not { } result
        ? string.Empty
        : result.Unmapped.Count == 0
            ? "No bytes differ outside the XDF"
            : $"{result.Unmapped.Count} {(result.Unmapped.Count == 1 ? "range" : "ranges")}, {result.UnmappedBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes outside the XDF ({result.DifferingBytes.ToString("N0", CultureInfo.InvariantCulture)} differ)";

    public IReadOnlyList<KindChip> Chips { get => _chips; private set => SetField(ref _chips, value); }

    /// <summary>The kind filter: "all", "map", "curve", "axis" or "value" (values, switches and flags).</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (KindFilters.Any(filter => filter.Key == value))
            {
                // Rebuild even when unchanged: clicking the active chip toggles it off on screen.
                SetField(ref _kind, value);
                RebuildList();
            }
        }
    }

    public ItemDifference? Selected
    {
        get => _selected;
        set
        {
            _showsBytes = false;
            SetField(ref _selected, value);
            Message = null;
            BuildDetail();
            RebuildList();
            OnPropertyChanged(nameof(ShowsBytes));
        }
    }

    /// <summary>True while the list of bytes outside the XDF is shown instead of an item.</summary>
    public bool ShowsBytes => _showsBytes;

    public bool ShowsTable => Selected?.Item is XdfTable && !_showsBytes;

    public bool ShowsValue => Selected is { Item: not XdfTable } && !_showsBytes;

    /// <summary>The grid shown: "delta" (B − A), "a" or "b".</summary>
    public string View
    {
        get => _view;
        set
        {
            if (value is "delta" or "a" or "b" && SetField(ref _view, value))
            {
                OnPropertyChanged(nameof(IsDeltaView));
                OnPropertyChanged(nameof(IsAView));
                OnPropertyChanged(nameof(IsBView));
                BuildGrid();
            }
        }
    }

    public bool IsDeltaView { get => View == "delta"; set { if (value) { View = "delta"; } else { OnPropertyChanged(); } } }

    public bool IsAView { get => View == "a"; set { if (value) { View = "a"; } else { OnPropertyChanged(); } } }

    public bool IsBView { get => View == "b"; set { if (value) { View = "b"; } else { OnPropertyChanged(); } } }

    public TableEditorViewModel? Grid { get => _grid; private set => SetField(ref _grid, value); }

    public string? AxisBanner => _table is { Axes.Count: > 0 } table ? string.Join("\n", table.Axes.Select(axis => axis.Describe())) : null;

    public string Units => Selected is null ? string.Empty : UnitsOf(Selected.Item);

    public string? Description => Selected?.Item.Description is { Length: > 0 } text ? text : null;

    /// <summary>"12 of 320 cells differ" for tables; null otherwise.</summary>
    public string? CellsText => Selected is { Item: XdfTable } item
        ? $"{item.ChangedCells} of {item.Cells} {(item.Cells == 1 ? "cell differs" : "cells differ")}"
        : null;

    public string? LargestText => Selected is { Item: XdfTable table, LargestChange: double largest }
        ? $"largest change {BinComparer.FormatDelta(largest, 0, table.Z, raw: false)} {Units}".TrimEnd()
        : null;

    public string? LegendMin { get; private set; }

    public string? LegendMax { get; private set; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetField(ref _isVisible, value) && value)
            {
                ReopenLast();
                if (_stale)
                {
                    Refresh();
                }
            }
        }
    }

    internal static string UnitsOf(XdfItem item)
    {
        var units = item switch
        {
            XdfTable table => table.Z.Units,
            XdfConstant constant => constant.Units,
            _ => string.Empty,
        };
        return units.Trim() == "-" ? string.Empty : units.Trim();
    }

    /// <summary>Opens B; a file that is not a 512 KiB bin is refused and the current B stays.</summary>
    public bool OpenB(string path)
    {
        try
        {
            var image = BinImage.Load(path);
            _b = image;
            FileB = Path.GetFileName(path);
            ErrorMessage = null;
            _tune.Settings.LastCompareBin = Path.GetFullPath(path);
            _tune.Settings.Save();
            OnPropertyChanged(nameof(HasB));
            OnPropertyChanged(nameof(NeedsB));
            Refresh();
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ErrorMessage = error.Message;
            return false;
        }
    }

    /// <summary>Compares A with B again; keeps the selected item when it still differs.</summary>
    public void Refresh()
    {
        _stale = false;
        OnPropertyChanged(nameof(FileA));
        var document = _tune.Document;
        FirmwareWarning = document is not null && _b is not null && FirmwareIdentifier.Identify(_b).SoftwareId is var idB && idB != document.Firmware.SoftwareId
            ? $"B is firmware {idB ?? "unknown"}, A is {document.Firmware.SoftwareId ?? "unknown"}. Items are compared at the same addresses, where B may hold something else."
            : null;
        Result = document is not null && _b is not null ? BinComparer.Compare(document.Xdf, document.Image, _b) : null;
        _tune.CompareWith(Result is null ? null : FileB, Result?.Items.Select(item => item.Item).ToHashSet());

        var keep = Selected is { } current ? Result?.Items.FirstOrDefault(item => item.Item == current.Item) : null;
        if (_showsBytes && Result is { Unmapped.Count: > 0 })
        {
            RebuildList();
            return;
        }

        _showsBytes = false;
        _selected = keep ?? Visible().FirstOrDefault();
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(ShowsBytes));
        BuildDetail();
        RebuildList();
    }

    public void ShowBytes()
    {
        _showsBytes = true;
        _selected = null;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(ShowsBytes));
        BuildDetail();
        RebuildList();
    }

    /// <summary>Writes B's value of the selected item into A through the undo history.</summary>
    public bool CopySelectedFromB()
    {
        if (Selected is not { } selected || _b is null || _tune.Document is not { } document || _tune.History is not { } history)
        {
            return false;
        }

        try
        {
            var plan = TableEdits.CopyFrom(document, selected.Item, _b);
            history.Apply(plan, $"Copy {selected.Item.Title} from {FileB}");
            var warnings = plan.Warnings.Count > 0 ? " " + string.Join(" ", plan.Warnings) : string.Empty;
            Message = $"Copied {selected.Item.Title} from B. Undo with Ctrl Z.{warnings}";
            return plan.Changes.Count > 0;
        }
        catch (EditRefusedException error)
        {
            Message = error.Message;
            return false;
        }
    }

    public void ShowInTune()
    {
        if (Selected is { } selected)
        {
            ShowInTuneRequested?.Invoke(this, selected.Item);
        }
    }

    private void ReopenLast()
    {
        if (_b is null && HasA && _tune.Settings.LastCompareBin is { } last && File.Exists(last))
        {
            OpenB(last);
        }
    }

    private void Watch(CalibrationDocument? document)
    {
        if (_watched is not null)
        {
            _watched.Changed -= OnDocumentChanged;
        }

        _watched = document;
        if (document is not null)
        {
            document.Changed += OnDocumentChanged;
        }
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Invalidate();

    private void Invalidate()
    {
        _stale = true;
        if (IsVisible || _tune.HasCompare)
        {
            Refresh();
        }
    }

    private IEnumerable<ItemDifference> Visible()
    {
        var kinds = KindFilters.Single(filter => filter.Key == Kind).Kinds;
        return Result?.Items.Where(item => kinds.Contains(item.Kind)) ?? Enumerable.Empty<ItemDifference>();
    }

    private void RebuildList()
    {
        Chips = Result is not { } result
            ? Array.Empty<KindChip>()
            : KindFilters.Select(filter => new KindChip(filter.Key, $"{filter.Name} {result.Items.Count(item => filter.Kinds.Contains(item.Kind))}", filter.Key == Kind)).ToList();

        Entries.Clear();
        string? category = null;
        foreach (var item in Visible())
        {
            if (item.Category != category)
            {
                category = item.Category;
                Entries.Add(new CompareGroup(category));
            }

            Entries.Add(new CompareRow(item, !_showsBytes && Selected?.Item == item.Item));
        }

        if (Result is { Unmapped.Count: > 0 })
        {
            Entries.Add(new CompareGroup("Outside the XDF"));
            Entries.Add(new CompareBytesRow(UnmappedSummary, _showsBytes));
        }
    }

    private void BuildDetail()
    {
        _table = Selected is { Item: XdfTable table } && _tune.Document is { } document && _b is not null
            ? BinComparer.CompareTable(document.Xdf, document.Image, _b, table)
            : null;
        BuildGrid();
        foreach (var name in new[] { nameof(ShowsTable), nameof(ShowsValue), nameof(AxisBanner), nameof(Units), nameof(Description), nameof(CellsText), nameof(LargestText) })
        {
            OnPropertyChanged(name);
        }
    }

    private void BuildGrid()
    {
        if (_table is not { } table || _showsBytes)
        {
            Grid = null;
            LegendMin = LegendMax = null;
        }
        else
        {
            Grid = View switch
            {
                "a" => TableEditorViewModel.Snapshot(table.A, table.Changed),
                "b" => TableEditorViewModel.Snapshot(table.B, table.Changed),
                // Colour already shows every change on B − A; the changed-cell dots would only add noise.
                _ => TableEditorViewModel.Snapshot(table.Delta, new HashSet<(int Row, int Col)>(), isDelta: true),
            };
            var z = table.A.Table.Z;
            var units = Units.Length > 0 ? " " + Units : string.Empty;
            var finite = Grid.Values.Physical.Cast<double>().Where(double.IsFinite).ToList();
            if (finite.Count == 0)
            {
                LegendMin = LegendMax = null;
            }
            else if (Grid.IsDelta)
            {
                var largest = finite.Max(Math.Abs);
                LegendMin = largest == 0 ? "no change" : $"{BinComparer.FormatDelta(-largest, 0, z, raw: false)}{units}, B lower";
                LegendMax = largest == 0 ? "no change" : $"{BinComparer.FormatDelta(largest, 0, z, raw: false)}{units}, B higher";
            }
            else
            {
                LegendMin = ValueCodec.Format(finite.Min(), 0, z.DecimalPlaces, null, 16).Replace('-', '−') + units;
                LegendMax = ValueCodec.Format(finite.Max(), 0, z.DecimalPlaces, null, 16).Replace('-', '−') + units;
            }
        }

        OnPropertyChanged(nameof(LegendMin));
        OnPropertyChanged(nameof(LegendMax));
    }
}
