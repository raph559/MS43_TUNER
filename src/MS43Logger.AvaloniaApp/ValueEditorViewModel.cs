using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public sealed record ChoiceItem(long Value, string Label)
{
    /// <summary>The choice as the list shows it; the raw number has its own column.</summary>
    public string Text => Label;
}

/// <summary>Editor for a single value, a configuration switch (with its choices) or a flag bit.</summary>
public sealed class ValueEditorViewModel : ViewModelBase, IDisposable
{
    private readonly CalibrationDocument _document;
    private readonly EditHistory _history;
    private ConstantValue _current;
    private bool _isOn;
    private string _input = string.Empty;
    private string? _message;
    private ChoiceItem? _selectedChoice;
    private bool _syncing;
    private bool _dragging;
    private double? _pendingValue;
    private string? _dragText;

    public ValueEditorViewModel(CalibrationDocument document, EditHistory history, XdfItem item, XdfCatalog catalog)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        Item = item ?? throw new ArgumentNullException(nameof(item));
        // The choices as this bin's overlay reads them (the current overlay names its LC/NLS/RAL triggers).
        Choices = item is XdfConstant constant ? catalog.Choices(constant, OverlayProtocol.IsCurrent(document.Image.Bytes))?.Select(choice => new ChoiceItem(choice.Value, choice.Label)).ToList() : null;
        Limits = item is XdfConstant limited
            ? EditLimits.For(document.Reader.Codec, limited.Math, limited.Data, limited.RangeLow, limited.RangeHigh)
            : new EditLimits(0, 1, 1, null, null);
        _current = new ConstantValue(0, double.NaN, string.Empty);
        Read();
        _document.Changed += OnDocumentChanged;
    }

    public XdfItem Item { get; }

    public bool IsFlag => Item is XdfFlag;

    public bool IsSwitch => Choices is not null;

    public bool IsValue => !IsFlag && !IsSwitch;

    public string Units => Item is XdfConstant constant && constant.Units != "-" ? constant.Units : string.Empty;

    public IReadOnlyList<ChoiceItem>? Choices { get; }

    /// <summary>What the value can hold: the storable range and step, and the XDF range where it narrows that.</summary>
    public EditLimits Limits { get; }

    public long RawValue => Current.Raw;

    /// <summary>The slider's low end: the XDF end where it narrows the storable range, else the storable end.</summary>
    public double SpanLow => Span().Low;

    public double SpanHigh => Span().High;

    /// <summary>The XDF ends that narrow the storable range, drawn as ticks on the slider; null where the XDF does not narrow it.</summary>
    public double? TickLow => NarrowingRange() is { } range && Limits.XdfLow is not null ? range.Low : null;

    public double? TickHigh => NarrowingRange() is { } range && Limits.XdfHigh is not null ? range.High : null;

    public string SpanLowLabel => WithUnits(Number(SpanLow));

    public string SpanHighLabel => WithUnits(Number(SpanHigh));

    /// <summary>
    /// "Ticks: the XDF range, 480 to 8160 rpm. Typed values from 0 to 65535 rpm are stored, with a warning outside the
    /// ticks." Shown only where the XDF range narrows the storable one.
    /// </summary>
    public string? LimitsLegend => NarrowingRange() is { } range
        ? $"Ticks: the XDF range, {WithUnits($"{Number(range.Low)} to {Number(range.High)}")}. "
          + $"Typed values from {WithUnits($"{Number(Limits.StorableMin)} to {Number(Limits.StorableMax)}")} are stored, with a warning outside the ticks."
        : null;

    /// <summary>The minus button's tooltip: one raw step down, "-32 rpm".</summary>
    public string StepDownTip => Limits.Step is double step ? "-" + WithUnits(Exact(step)) : "-1 raw";

    public string StepUpTip => Limits.Step is double step ? "+" + WithUnits(Exact(step)) : "+1 raw";

    /// <summary>The value under the slider's thumb while it is dragged; written only on release.</summary>
    public double? PendingValue
    {
        get => _pendingValue;
        private set
        {
            if (SetField(ref _pendingValue, value))
            {
                OnPropertyChanged(nameof(ReadingText));
            }
        }
    }

    /// <summary>"Release to set 4000 rpm. Esc cancels." while dragging.</summary>
    public string? DragText { get => _dragText; private set => SetField(ref _dragText, value); }

    /// <summary>The Data 56 reading above the slider: the value under the thumb while it is dragged, else the stored one.</summary>
    public string ReadingText => PendingValue is double pending ? Number(pending) : Current.Display;

    /// <summary>The reading's unit after an en space, so the two keep their gap on one baseline.</summary>
    public string ReadingUnits => Units.Length > 0 ? "\u2002" + Units : string.Empty;

    public bool IsDragging => _dragging;

    /// <summary>The slider's tooltip: "Drag to set, or step with the arrow keys (32 rpm) and Page Up, Page Down (320 rpm); …".</summary>
    public string SliderTip => Limits.Step is double step
        ? $"Drag to set, or step with the arrow keys ({WithUnits(Exact(step))}) and Page Up, Page Down ({WithUnits(Exact(step * 10))}); Home and End go to the ends, Esc cancels a drag"
        : "Drag to set, or step with the arrow keys (one raw step) and Page Up, Page Down (ten raw steps); Home and End go to the ends, Esc cancels a drag";

    /// <summary>Moves the value by whole raw steps, up meaning a higher value, and writes it at once as one undo step.</summary>
    public bool StepBy(int rawSteps)
    {
        if (Item is not XdfConstant constant || rawSteps == 0)
        {
            return false;
        }

        var codec = _document.Reader.Codec;
        var (min, max) = RawLimits(constant.Data);
        var rises = Current.Raw < max
            ? codec.ToPhysical(constant.Math, Current.Raw + 1) >= Current.Physical
            : codec.ToPhysical(constant.Math, Current.Raw - 1) <= Current.Physical;
        var target = Math.Clamp(Current.Raw + (rises ? rawSteps : -rawSteps), min, max);
        var delta = (target - Current.Raw).ToString(CultureInfo.InvariantCulture);
        return target != Current.Raw
            && Write(() => TableEdits.SetConstantRaw(_document, constant, target), $"Step {constant.Title} by {delta} raw");
    }

    /// <summary>Home and End: the span's low or high end.</summary>
    public bool JumpTo(bool toHigh)
    {
        if (Item is not XdfConstant constant)
        {
            return false;
        }

        var value = toHigh ? SpanHigh : SpanLow;
        return Write(() => TableEdits.SetConstant(_document, constant, value), $"Set {constant.Title} to {WithUnits(Number(value))}");
    }

    public void BeginDrag()
    {
        _dragging = true;
        PendingValue = null;
        DragText = null;
    }

    /// <summary>Puts the thumb's value, snapped to one the ECU can store, in the reading and the key line.</summary>
    public void DragTo(double physical)
    {
        if (!_dragging || Item is not XdfConstant constant || !double.IsFinite(physical))
        {
            return;
        }

        var codec = _document.Reader.Codec;
        try
        {
            PendingValue = codec.ToPhysical(constant.Math, codec.ToRaw(constant.Math, constant.Data, physical));
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            // An equation the codec cannot invert: the drag shows nothing to set, as TableEdits refuses the write.
            PendingValue = null;
            DragText = $"{constant.Title} cannot be set by dragging: {error.Message}";
            return;
        }

        DragText = $"Release to set {WithUnits(Number(PendingValue.Value))}. Esc cancels.";
    }

    /// <summary>Writes the dragged value as one undo step.</summary>
    public bool CommitDrag()
    {
        var pending = PendingValue;
        CancelDrag();
        return pending is double value && Item is XdfConstant constant
            && Write(() => TableEdits.SetConstant(_document, constant, value), $"Set {constant.Title} to {WithUnits(Number(value))}");
    }

    public void CancelDrag()
    {
        _dragging = false;
        PendingValue = null;
        DragText = null;
    }

    private (double Low, double High) Span()
    {
        var (low, high) = NarrowingRange() ?? (Limits.StorableMin, Limits.StorableMax);

        // A value outside the span by more than half a step widens it, so the thumb always shows where the value is.
        var half = (Limits.Step ?? 0) / 2;
        var value = Current.Physical;
        if (double.IsFinite(value))
        {
            low = value < low - half ? value : low;
            high = value > high + half ? value : high;
        }

        return (low, high);
    }

    // The XDF range where it narrows the storable one; never inverted (a malformed XDF range falls back to the storable range).
    private (double Low, double High)? NarrowingRange()
    {
        if (Limits.XdfLow is null && Limits.XdfHigh is null)
        {
            return null;
        }

        var low = Limits.XdfLow ?? Limits.StorableMin;
        var high = Limits.XdfHigh ?? Limits.StorableMax;
        return low < high ? (low, high) : null;
    }

    private string Number(double value) =>
        ValueCodec.Format(value, 0, Item is XdfConstant constant ? constant.DecimalPlaces : 0, null, 16);

    private string WithUnits(string text) => Units.Length > 0 ? $"{text} {Units}" : text;

    private static string Exact(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static (long Min, long Max) RawLimits(XdfData data)
    {
        var bits = Math.Clamp(data.ElementBits, 1, 32);
        return data.Signed ? (-(1L << (bits - 1)), (1L << (bits - 1)) - 1) : (0, (1L << bits) - 1);
    }

    public ConstantValue Current
    {
        get => _current;
        private set
        {
            if (SetField(ref _current, value))
            {
                OnPropertyChanged(nameof(ReadingText));
                OnPropertyChanged(nameof(RawValue));
                OnPropertyChanged(nameof(SpanLow));
                OnPropertyChanged(nameof(SpanHigh));
                OnPropertyChanged(nameof(SpanLowLabel));
                OnPropertyChanged(nameof(SpanHighLabel));
            }
        }
    }

    public string? Message { get => _message; private set => SetField(ref _message, value); }

    public string Input { get => _input; set => SetField(ref _input, value ?? string.Empty); }

    public ChoiceItem? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (SetField(ref _selectedChoice, value) && !_syncing && value is not null && Item is XdfConstant constant)
            {
                Write(() => TableEdits.SetConstantRaw(_document, constant, value.Value), $"Set {constant.Title} to {value.Text}");
            }
        }
    }

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetField(ref _isOn, value) && !_syncing && Item is XdfFlag flag)
            {
                Write(() => TableEdits.SetFlag(_document, flag, value), $"{(value ? "Turn on" : "Turn off")} {flag.Title}");
            }
        }
    }

    /// <summary>
    /// Stores the typed value (snapped to the nearest value the ECU can hold). The field empties once the value is
    /// written, since it is never prefilled; a refused or unparsable entry stays for correction.
    /// </summary>
    public bool Apply()
    {
        if (Item is not XdfConstant constant)
        {
            return false;
        }

        if (!double.TryParse(Input.Replace('−', '-').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            Message = $"'{Input}' is not a number.";
            return false;
        }

        if (!Write(() => TableEdits.SetConstant(_document, constant, value), $"Set {constant.Title} to {value.ToString(CultureInfo.InvariantCulture)}"))
        {
            return false;
        }

        Input = string.Empty;
        return true;
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;

    /// <summary>Refuses an edit before it is applied: the reason, or null to apply it (the Fun tab's firmware limits offline).</summary>
    public Func<EditPlan, string?>? Guard { get; set; }

    private bool Write(Func<EditPlan> plan, string label)
    {
        try
        {
            var edit = plan();
            if (edit.Changes.Count > 0 && Guard?.Invoke(edit) is { } refused)
            {
                Message = refused;
                Read();
                return false;
            }

            _history.Apply(edit, label);
            Message = edit.Warnings.Count > 0 ? string.Join(" ", edit.Warnings) : edit.Changes.Count == 0 ? "Nothing changed: it already holds that value." : null;
            return edit.Changes.Count > 0;
        }
        catch (EditRefusedException error)
        {
            Message = error.Message;
            Read();
            return false;
        }
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Read();

    private void Read()
    {
        _syncing = true;
        try
        {
            switch (Item)
            {
                case XdfConstant constant:
                    // The choice first: whoever listens to Current (the inspector's facts) reads it.
                    var current = _document.Reader.ReadConstant(constant);
                    SelectedChoice = Choices?.FirstOrDefault(choice => choice.Value == current.Raw);
                    Current = current;
                    break;
                case XdfFlag flag:
                    IsOn = _document.Reader.ReadFlag(flag);
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}
