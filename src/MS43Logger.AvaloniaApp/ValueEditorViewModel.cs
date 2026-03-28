using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public sealed record ChoiceItem(long Value, string Label)
{
    public string Text => $"{Value}: {Label}";
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

    public ValueEditorViewModel(CalibrationDocument document, EditHistory history, XdfItem item, XdfCatalog catalog)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Choices = item is XdfConstant constant ? catalog.Choices(constant)?.Select(choice => new ChoiceItem(choice.Value, choice.Label)).ToList() : null;
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

    public ConstantValue Current { get => _current; private set => SetField(ref _current, value); }

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

    /// <summary>Stores the typed value (snapped to the nearest value the ECU can hold).</summary>
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

        return Write(() => TableEdits.SetConstant(_document, constant, value), $"Set {constant.Title} to {value.ToString(CultureInfo.InvariantCulture)}");
    }

    public void Dispose() => _document.Changed -= OnDocumentChanged;

    private bool Write(Func<EditPlan> plan, string label)
    {
        try
        {
            var edit = plan();
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
                    Current = _document.Reader.ReadConstant(constant);
                    Input = Current.Display;
                    SelectedChoice = Choices?.FirstOrDefault(choice => choice.Value == Current.Raw);
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
