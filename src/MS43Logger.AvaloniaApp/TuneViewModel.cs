using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MS43Logger.Core.Tuning;

namespace MS43Logger.AvaloniaApp;

public abstract record RailEntry;

public sealed record RailGroup(string Title) : RailEntry;

public sealed record RailCategory(string Name, int Count, bool IsExpanded) : RailEntry;

public sealed record Fact(string Label, string Text);

public enum SaveChoice
{
    Save,
    DontSave,
    Cancel,
}

public sealed record RailItem(XdfItem Item, ItemKind Kind, bool IsPinned, bool IsModified, bool IsCurrent, bool IsIndented) : RailEntry
{
    public string Title => Item.Title;
}
