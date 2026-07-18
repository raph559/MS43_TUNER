namespace MS43Logger.AvaloniaApp.Tests;

public class ConnectionMessagesTests
{
    private const string Csv = @"D:\logs\ms43-20261019-2225.csv";

    private static readonly StopContext Logging = new("COM1", "ms43-20261019-2225.csv", 9576, new TimeSpan(0, 4, 12));

    private static readonly StopContext NotLogging = new("COM1", null, 9576, new TimeSpan(0, 4, 12));

    // One row per message of system 7.2: what the app says, the text the system fixes, and the banner's action.
    public static TheoryData<ConnectionMessage, string, LiveAction> Messages => new()
    {
        { ConnectionMessages.Retrying(1), "The ECU did not answer (1 of 3). Trying again.", LiveAction.None },
        { ConnectionMessages.Retrying(2), "The ECU did not answer (2 of 3). Trying again.", LiveAction.None },
        {
            ConnectionMessages.NotConnected(new CsvWriteException(Csv, new IOException("Access to the path is denied.")), "COM1", Csv),
            @"Not connected: cannot write D:\logs\ms43-20261019-2225.csv (Access to the path is denied). Choose another file, then connect.",
            LiveAction.Browse
        },
        {
            ConnectionMessages.NotConnected(new UnauthorizedAccessException("Access to the port 'COM1' is denied."), "COM1", Csv),
            "Not connected: COM1 is in use by another program. Close it, then connect.", LiveAction.None
        },
        {
            ConnectionMessages.NotConnected(new TimeoutException("The operation has timed out."), "COM1", Csv),
            "Not connected: the ECU did not answer on COM1. Turn the ignition on, then connect.", LiveAction.None
        },
        {
            ConnectionMessages.NotConnected(new InvalidOperationException("The port is closed."), "COM1", Csv),
            "Not connected: The port is closed.", LiveAction.None
        },
        {
            ConnectionMessages.Stopped(new TimeoutException("No reply."), Logging with { Samples = 0 }),
            "Not connected: the ECU refused the faster baud rate. Turn the engine off, connect, then start the engine.", LiveAction.None
        },
        {
            ConnectionMessages.Stopped(new TimeoutException("No reply."), Logging),
            "Logging stopped: the ECU stopped answering on COM1 (3 timeouts in a row). ms43-20261019-2225.csv is closed with 9576 rows.", LiveAction.None
        },
        {
            ConnectionMessages.Stopped(new TimeoutException("No reply."), NotLogging),
            "Logging stopped: the ECU stopped answering on COM1 (3 timeouts in a row).", LiveAction.None
        },
        {
            ConnectionMessages.Stopped(new CsvWriteException(Csv, new IOException("There is not enough space on the disk.")), Logging),
            "Logging stopped: writing ms43-20261019-2225.csv failed (There is not enough space on the disk). The rows written before 00:04:12 are kept.",
            LiveAction.Browse
        },
        {
            ConnectionMessages.Stopped(new InvalidOperationException("The frame checksum is wrong."), Logging),
            "Logging stopped: The frame checksum is wrong. ms43-20261019-2225.csv is closed with 9576 rows.", LiveAction.None
        },
        {
            ConnectionMessages.Stopped(new InvalidOperationException("The frame checksum is wrong."), NotLogging),
            "Logging stopped: The frame checksum is wrong.", LiveAction.None
        },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void EveryMessageMatchesTheSystem(ConnectionMessage message, string text, LiveAction action)
    {
        Assert.Equal(text, message.Text);
        Assert.Equal(action, message.Action);
    }

    [Fact]
    public void StaleLineCountsSeconds() =>
        Assert.Equal("The values below are from the last sample, 8 s ago.", ConnectionMessages.StaleLine(8));
}
