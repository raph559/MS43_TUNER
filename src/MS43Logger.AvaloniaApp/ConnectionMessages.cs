using System;
using System.Globalization;
using MS43Logger.Core;

namespace MS43Logger.AvaloniaApp;

/// <summary>What a Live banner offers besides its text: a way out the person can take from the banner.</summary>
public enum LiveAction
{
    None,
    Browse,
    RescanPorts,
}

/// <summary>A banner's sentence (system 7.2) and the action beside it.</summary>
public sealed record ConnectionMessage(string Text, LiveAction Action = LiveAction.None);

/// <summary>What the stopped-logging message names: the port, the CSV file (null when none was written), the samples
/// shown and the time the session ran.</summary>
public sealed record StopContext(string Port, string? CsvFile, long Samples, TimeSpan Elapsed);

/// <summary>Creating or writing the CSV log failed; the message names the file and the reason.</summary>
public sealed class CsvWriteException(string path, Exception inner) : Exception(inner.Message, inner)
{
    public string Path { get; } = path;
}

/// <summary>The Live screen's connection and logging messages, word for word as system 7.2 writes them.</summary>
public static class ConnectionMessages
{
    public static ConnectionMessage Retrying(int timeouts) =>
        new($"The ECU did not answer ({timeouts} of {LiveLogger.TimeoutLimit}). Trying again.");

    /// <summary>Connect failed before logging began.</summary>
    public static ConnectionMessage NotConnected(Exception error, string port, string? csvPath) => error switch
    {
        CsvWriteException csv => new($"Not connected: cannot write {csv.Path} ({Reason(csv.InnerException ?? csv)}). Choose another file, then connect.", LiveAction.Browse),
        UnauthorizedAccessException => new($"Not connected: {port} is in use by another program. Close it, then connect."),
        TimeoutException => new($"Not connected: the ECU did not answer on {port}. Turn the ignition on, then connect."),
        _ => new($"Not connected: {Reason(error)}."),
    };

    /// <summary>The logger stopped; with no sample at all, the timeouts mean the ECU refused the faster baud rate.</summary>
    public static ConnectionMessage Stopped(Exception error, StopContext context)
    {
        var closed = context.CsvFile is { } file
            ? $" {file} is closed with {context.Samples.ToString(CultureInfo.InvariantCulture)} rows."
            : string.Empty;
        return error switch
        {
            TimeoutException when context.Samples == 0 =>
                new("Not connected: the ECU refused the faster baud rate. Turn the engine off, connect, then start the engine."),
            TimeoutException => new($"Logging stopped: the ECU stopped answering on {context.Port} ({LiveLogger.TimeoutLimit} timeouts in a row).{closed}"),
            CsvWriteException csv => new(
                $"Logging stopped: writing {context.CsvFile ?? System.IO.Path.GetFileName(csv.Path)} failed ({Reason(csv.InnerException ?? csv)}). " +
                $"The rows written before {Clock(context.Elapsed)} are kept.",
                LiveAction.Browse),
            _ => new($"Logging stopped: {Reason(error)}.{closed}"),
        };
    }

    /// <summary>The ECU went quiet after answering (a key-off): the app reconnects on its own and the log stays open.</summary>
    public static ConnectionMessage Reconnecting(string port, string? csvFile, long rows) => new(
        $"Reconnecting: the ECU stopped answering on {port}. Trying again every second" +
        (csvFile is { } file ? $"; {file} stays open with {rows.ToString(CultureInfo.InvariantCulture)} rows." : "."));

    /// <summary>The stopped banner's second line, renewed every second while the values below are frozen.</summary>
    public static string StaleLine(int seconds) => $"The values below are from the last sample, {seconds.ToString(CultureInfo.InvariantCulture)} s ago.";

    // An exception's message without its trailing period, so it sits inside a sentence.
    private static string Reason(Exception error) => error.Message.Trim().TrimEnd('.');

    private static string Clock(TimeSpan elapsed) =>
        string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
}
