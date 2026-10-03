namespace MS43Logger.AvaloniaApp;

/// <summary>A category of the live map list, as the Tune rail has them: its name, how many live maps it holds, open or not.</summary>
public sealed record LiveRailCategory(string Name, int Count, bool IsExpanded);
