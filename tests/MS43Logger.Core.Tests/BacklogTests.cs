using MS43Logger.Core;

namespace MS43Logger.Core.Tests;

/// <summary>
/// The samples waiting for the screen (owner, 2026-10-28): with no pause between polls, a source that answers at once must not
/// pile them up in memory; the logger waits once the backlog is full, and a disconnect frees it.
/// </summary>
public sealed class BacklogTests
{
    [Fact]
    public void TheLoggerWaitsOnceTheBacklogIsFull_AndGoesOnWhenTheScreenCatchesUp()
    {
        var backlog = new Backlog(limit: 3);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(backlog.Enter(CancellationToken.None));
        }

        var fourth = Task.Run(() => backlog.Enter(CancellationToken.None));
        Assert.False(fourth.Wait(150));

        backlog.Leave();
        Assert.True(fourth.Wait(2000));
        Assert.True(fourth.Result);
        Assert.Equal(3, backlog.Waiting);
    }

    [Fact]
    public void ADisconnectFreesAWaitingLogger()
    {
        var backlog = new Backlog(limit: 1);
        Assert.True(backlog.Enter(CancellationToken.None));
        using var stop = new CancellationTokenSource();
        var waiting = Task.Run(() => backlog.Enter(stop.Token));
        Assert.False(waiting.Wait(100));

        stop.Cancel();
        Assert.True(waiting.Wait(2000));
        Assert.False(waiting.Result);
    }
}
