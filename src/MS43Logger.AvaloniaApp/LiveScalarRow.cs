using System;
using System.Collections.Generic;
using System.Linq;
using MS43Logger.Core.LiveTuning;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public sealed class LiveScalarRow(string label, ValueEditorViewModel editor) : IDisposable
{
    public string Label { get; } = label;
    public ValueEditorViewModel Editor { get; } = editor;
    public string Key => Editor.Item.Title;
    public string Description => Editor.Item.Description;
    public void Dispose() => Editor.Dispose();

    /// <summary>
    /// One row per field of a settings group, editing <paramref name="document"/> through <paramref name="history"/>: the live
    /// page's copy, or the open bin. NLS mode 2 gets its description when the XDF lacks it.
    /// </summary>
    public static IReadOnlyList<LiveScalarRow> Build(CalibrationDocument document, EditHistory history, OverlayMap map, IReadOnlyList<XdfConstant> constants)
    {
        var catalog = new XdfCatalog(document.Xdf);
        return map.ScalarGroup!.Fields.Select((field, i) =>
        {
            var constant = constants[i];
            if (constant.Title == "c_conf_nls" && catalog.Choices(constant)?.All(choice => choice.Value != 2) == true)
                constant = constant with { Description = constant.Description + "\n2: No Lift Shift activated when cruise control LED is active and clutch is pressed" };
            return new LiveScalarRow(field.Label, new ValueEditorViewModel(document, history, constant, catalog));
        }).ToArray();
    }
}
