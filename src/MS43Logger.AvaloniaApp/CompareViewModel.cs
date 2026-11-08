using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public abstract record CompareEntry;

public sealed record CompareGroup(string Title) : CompareEntry;

/// <summary>One differing item in the list.</summary>
public sealed record CompareRow(ItemDifference Difference, bool IsCurrent) : CompareEntry
{
    public string Title => Difference.Item.Title;

    public string Description => Difference.Item.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Difference.Item.Description);

    /// <summary>The hover card's facts line, as on the Tune rail: "Map, 20 × 16, °CRK, at 0x77315".</summary>
    public string Detail => ItemFacts.Line(Difference.Item, Difference.Kind);

    /// <summary>
    /// "12 of 320" for tables; "3520 → 4512" for values, switches and flags, read as "was, now": B (the bin compared with)
    /// then A (the bin tuned), the way A − B reads.
    /// </summary>
    public string Summary => Difference.Item is XdfTable
        ? $"{Difference.ChangedCells} of {Difference.Cells}"
        : $"{Difference.ValueB} → {Difference.ValueA}";

    public string Units => CompareViewModel.UnitsOf(Difference.Item);
}

/// <summary>The list entry that opens the bytes that differ outside every XDF item.</summary>
public sealed record CompareBytesRow(string Summary, bool IsCurrent) : CompareEntry;

/// <summary>One segment of the kind filter (system 5.15): the kind's name over its count of differing items.</summary>
public sealed record KindChip(string Key, string Name, int Count, bool IsOn)
{
    /// <summary>What a screen reader says for the segment: "Maps, 12".</summary>
    public string Spoken => $"{Name}, {Count}";
}

/// <summary>The Compare screen: the open bin (A) against a second bin (B) through A's XDF.</summary>
public sealed class CompareViewModel : ViewModelBase, ISidePanel
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
    private string _searchText = string.Empty;
    private string? _scaleCaption;
    private (double Low, double High) _scaleSpan;
    private (string? Min, string? Mid, string? Max) _scaleLabels;

    public CompareViewModel(TuneViewModel tune)
    {
        _tune = tune ?? throw new ArgumentNullException(nameof(tune));
        tune.DocumentOpened += (_, _) =>
        {
            Watch(tune.Document);
            OnPropertyChanged(nameof(FileA));
            OnPropertyChanged(nameof(NoBText));
            OnPropertyChanged(nameof(HasA));
            OnPropertyChanged(nameof(NeedsB));

            // The remembered B opens with A, not when the Compare tab is first shown, so Tune's compare strip and
            // "Differs from B" are there from the start. Opening it compares already; otherwise compare again.
            var before = _b;
            ReopenLast();
            if (ReferenceEquals(before, _b))
            {
                Invalidate();
            }
        };
        Watch(tune.Document);
        tune.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TuneViewModel.DiffersOnly) && tune.DiffersOnly && _stale)
            {
                Refresh();
            }

            // One A − B setting for both screens: a change made in Tune redraws Compare's grid too.
            if (e.PropertyName == nameof(TuneViewModel.DeltaMode))
            {
                foreach (var name in new[] { nameof(DeltaMode), nameof(IsDeltaUnits), nameof(IsDeltaRaw), nameof(IsDeltaPercent), nameof(LargestText) })
                {
                    OnPropertyChanged(name);
                }

                BuildGrid();
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

    /// <summary>"18 ranges, 11907 bytes outside the XDF (11436 differ)", no digit grouping (system 3.3); ranges may span a few equal bytes.</summary>
    public string UnmappedSummary => Result is not { } result
        ? string.Empty
        : result.Unmapped.Count == 0
            ? "No bytes differ outside the XDF"
            : $"{result.Unmapped.Count} {(result.Unmapped.Count == 1 ? "range" : "ranges")}, {result.UnmappedBytes.ToString(CultureInfo.InvariantCulture)} bytes outside the XDF ({result.DifferingBytes.ToString(CultureInfo.InvariantCulture)} differ)";

    /// <summary>The No B state's line (system 7.3): "A is CAR.bin. B can be any 512 KiB MS43 bin; both are read through A's XDF."</summary>
    public string NoBText => $"A is {FileA}. B can be any 512 KiB MS43 bin; both are read through A's XDF.";

    /// <summary>A range of the bytes outside the XDF, "0x03C43 to 0x03C9B", both ends included.</summary>
    public static string RangeText(UnmappedRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        return string.Format(CultureInfo.InvariantCulture, "0x{0:X5} to 0x{1:X5}", range.Offset, range.End);
    }

    /// <summary>Filters the list by item title and description (system 5.15), as the Tune rail's search does.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                RebuildList();
            }
        }
    }

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

    /// <summary>A switch's values are its choices, sentences that wrap as text instead of a number reading.</summary>
    public bool ValuesAreChoices => ShowsValue && Selected?.Kind == ItemKind.Switch;

    /// <summary>The grid shown: "delta" (A − B), "a" or "b".</summary>
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

    public AppSettings Settings => _tune.Settings;

    /// <summary>How A − B reads, as in TunerPro's compare: in the map's units, in stored counts, or as a percentage of B. Remembered.</summary>
    public DeltaMode DeltaMode
    {
        get => _tune.DeltaMode;
        set => _tune.DeltaMode = value;
    }

    public bool IsDeltaUnits { get => DeltaMode == DeltaMode.Units; set { if (value) { DeltaMode = DeltaMode.Units; } else { OnPropertyChanged(); } } }

    public bool IsDeltaRaw { get => DeltaMode == DeltaMode.Raw; set { if (value) { DeltaMode = DeltaMode.Raw; } else { OnPropertyChanged(); } } }

    public bool IsDeltaPercent { get => DeltaMode == DeltaMode.Percent; set { if (value) { DeltaMode = DeltaMode.Percent; } else { OnPropertyChanged(); } } }

    /// <summary>The Compare list's width as dragged, or null for the layout's own (280 px, 224 below 1280).</summary>
    public double? RailWidth => _tune.Settings.CompareRailWidth;

    /// <summary>True while the Compare list is folded to its strip (Ctrl B). Remembered between runs.</summary>
    public bool RailCollapsed => _tune.Settings.CompareRailCollapsed;

    public void SetRailWidth(double? width, bool save)
    {
        _tune.Settings.CompareRailWidth = width is double wanted ? Math.Clamp(Math.Round(wanted), SidePanel.MinWidth, SidePanel.MaxWidth) : null;
        if (save)
        {
            _tune.Settings.Save();
        }

        OnPropertyChanged(nameof(RailWidth));
    }

    public void SetRailCollapsed(bool collapsed)
    {
        if (_tune.Settings.CompareRailCollapsed == collapsed)
        {
            return;
        }

        _tune.Settings.CompareRailCollapsed = collapsed;
        _tune.Settings.Save();
        OnPropertyChanged(nameof(RailCollapsed));
    }

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

    /// <summary>"largest change +61.1 °CRK", in counts ("+163") or as a percentage of B ("+190.4 %") by <see cref="DeltaMode"/>.</summary>
    public string? LargestText
    {
        get
        {
            if (Selected is not { Item: XdfTable table, LargestChange: double largest })
            {
                return null;
            }

            if (DeltaMode == DeltaMode.Units || _table is not { } compared)
            {
                return $"largest change {BinComparer.FormatDelta(largest, 0, table.Z, raw: false)} {Units}".TrimEnd();
            }

            var values = BinComparer.Difference(compared, DeltaMode).Physical.Cast<double>().Where(double.IsFinite).ToList();
            if (values.Count == 0)
            {
                return null;
            }

            var most = values.MaxBy(Math.Abs);
            return $"largest change {BinComparer.FormatDelta(most, (long)most, table.Z, DeltaMode)}";
        }
    }

    /// <summary>
    /// The line under the grid (system 2.5, 5.15): "A − B: -21.0 to +61.1 °CRK, 293 of 320 cells differ" on A − B, and
    /// "Colour: -23.6 to 72.0 °CRK." on A or B, whose cells take the map colours.
    /// </summary>
    public string? ScaleCaption { get => _scaleCaption; private set => SetField(ref _scaleCaption, value); }

    /// <summary>The A − B bar's ends as signed fractions of the largest difference, so it shows only the mixes in use.</summary>
    public double ScaleLow => _scaleSpan.Low;

    public double ScaleHigh => _scaleSpan.High;

    /// <summary>The A or B scale's labels: the lowest, middle and highest value shown.</summary>
    public string? ScaleMinLabel => _scaleLabels.Min;

    public string? ScaleMidLabel => _scaleLabels.Mid;

    public string? ScaleMaxLabel => _scaleLabels.Max;

    /// <summary>"A − B: -992 rpm" under a value's two readings; null for tables, switches and flags.</summary>
    public string? ValueDeltaText => Selected is { Item: XdfConstant constant, Kind: ItemKind.Value, LargestChange: double delta } && !_showsBytes
        ? $"A − B: {Signed(delta, constant.DecimalPlaces)}{(Units.Length > 0 ? " " + Units : string.Empty)}"
        : null;

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
    /// <summary>B's file in full, or null without a B.</summary>
    public string? PathB { get; private set; }

    /// <summary>Asks before a swap (the window shows the dialog); without it, nothing is swapped.</summary>
    public Func<string, Task<bool>>? AskToSwap { get; set; }

    /// <summary>
    /// Swaps A and B: B's file opens on Tune as the bin edited (with the same XDF) and A's becomes B. Asks first, then asks
    /// about A's unsaved edits as opening any other bin does; either answer of Cancel leaves both as they were.
    /// </summary>
    public async Task<bool> SwapAsync()
    {
        if (_tune.Document is not { Path: { } a } || PathB is not { } b || AskToSwap is null)
        {
            return false;
        }

        // Tune's map windows edit A, which is about to go: they take no edits from the questions to the new A.
        using var hold = _tune.HoldMapWindows();
        if (!await AskToSwap($"Swap A and B? {Path.GetFileName(b)} opens as A, the bin you edit, and {Path.GetFileName(a)} becomes B.")
            || !await _tune.ConfirmDiscardAsync())
        {
            return false;
        }

        await _tune.OpenAsync(b, _tune.XdfPath);
        if (_tune.Document?.Path != Path.GetFullPath(b))
        {
            // Tune says why B did not open, and A is still the bin there.
            return false;
        }

        return OpenB(a);
    }

    public bool OpenB(string path)
    {
        try
        {
            var image = BinImage.Load(path);
            _b = image;
            FileB = Path.GetFileName(path);
            PathB = Path.GetFullPath(path);
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
            // System 7.3: "Not opened: stock.bin is 262144 bytes. An MS43 bin is exactly 524288 bytes (512 KiB)."
            ErrorMessage = "Not opened: " + error.Message;
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
        _tune.CompareWith(Result is null ? null : FileB, Result?.Items.Select(item => item.Item).ToHashSet(), Result is null ? null : _b);

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
        // The same search as the Tune rail: every word anywhere in the item (title, description, category, units, address).
        var catalog = _tune.Catalog;
        return Result?.Items.Where(item => kinds.Contains(item.Kind) && (catalog?.Matches(item.Item, SearchText) ?? true))
               ?? Enumerable.Empty<ItemDifference>();
    }

    private void RebuildList()
    {
        Chips = Result is not { } result
            ? Array.Empty<KindChip>()
            : KindFilters.Select(filter => new KindChip(filter.Key, filter.Name, result.Items.Count(item => filter.Kinds.Contains(item.Kind)), filter.Key == Kind)).ToList();

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
        foreach (var name in new[] { nameof(ShowsTable), nameof(ShowsValue), nameof(ValuesAreChoices), nameof(AxisBanner), nameof(Units), nameof(Description), nameof(CellsText), nameof(LargestText), nameof(ValueDeltaText) })
        {
            OnPropertyChanged(name);
        }
    }

    private void BuildGrid()
    {
        ScaleCaption = null;
        _scaleSpan = (0, 0);
        _scaleLabels = (null, null, null);
        if (_table is not { } table || _showsBytes)
        {
            Grid = null;
        }
        else
        {
            Grid = View switch
            {
                "a" => TableEditorViewModel.Snapshot(table.A, table.Changed, mark: CellMark.Differs),
                "b" => TableEditorViewModel.Snapshot(table.B, table.Changed, mark: CellMark.Differs),
                // Colour already shows every change on A − B; the changed-cell dots would only add noise.
                _ => TableEditorViewModel.Snapshot(BinComparer.Difference(table, DeltaMode), new HashSet<(int Row, int Col)>(), isDelta: true, deltaMode: DeltaMode),
            };
            var z = table.A.Table.Z;
            var units = Units.Length > 0 ? " " + Units : string.Empty;
            var finite = Grid.Values.Physical.Cast<double>().Where(double.IsFinite).ToList();
            if (finite.Count > 0 && Grid.IsDelta)
            {
                // The range of A − B in this item, signed as the cells print it, then how many cells differ.
                var (low, high) = (finite.Min(), finite.Max());
                var largest = finite.Max(Math.Abs);
                _scaleSpan = largest > 0 ? (low / largest, high / largest) : (0, 0);
                ScaleCaption = DeltaMode switch
                {
                    DeltaMode.Raw => $"A − B in counts: {BinComparer.FormatDelta(low, (long)low, z, DeltaMode.Raw)} to {BinComparer.FormatDelta(high, (long)high, z, DeltaMode.Raw)}, {CellsText}",
                    DeltaMode.Percent => $"A − B as % of B: {BinComparer.FormatDelta(low, 0, z, DeltaMode.Percent)} to {BinComparer.FormatDelta(high, 0, z, DeltaMode.Percent)}, {CellsText}",
                    _ => $"A − B: {BinComparer.FormatDelta(low, 0, z, raw: false)} to {BinComparer.FormatDelta(high, 0, z, raw: false)}{units}, {CellsText}",
                };
            }
            else if (finite.Count > 0)
            {
                string Number(double value) => ValueCodec.Format(value, 0, z.DecimalPlaces, null, 16);
                var range = Grid.ColourRange;
                _scaleLabels = (Number(range.Min), Number((range.Min + range.Max) / 2), Number(range.Max));
                ScaleCaption = $"Colour: {Number(range.Min)} to {Number(range.Max)}{units}.";
            }
        }

        foreach (var name in new[] { nameof(ScaleLow), nameof(ScaleHigh), nameof(ScaleMinLabel), nameof(ScaleMidLabel), nameof(ScaleMaxLabel) })
        {
            OnPropertyChanged(name);
        }
    }

    // A signed difference in the item's decimals: "-992", "+0.75", and "0" for none.
    private static string Signed(double delta, int? decimals)
    {
        var size = ValueCodec.Format(Math.Abs(delta), 0, decimals, null, 16);
        return delta == 0 ? size : (delta > 0 ? "+" : "-") + size;
    }
}
