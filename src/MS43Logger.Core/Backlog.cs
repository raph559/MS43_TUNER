namespace MS43Logger.Core;

/// <summary>
/// The samples handed to the screen and not shown yet. Without a pause between polls, a source that answers
/// at once would pile them up in memory: <see cref="Enter"/> waits while <c>limit</c> are waiting, so the logger slows to the
/// screen's pace and no sample is dropped. A real line, about 25 samples a second, never fills it.
/// </summary>
public sealed class Backlog(int limit)
{
    private readonly SemaphoreSlim _room = new(limit, limit);

    /// <summary>How many samples wait for the screen.</summary>
    public int Waiting => limit - _room.CurrentCount;

    /// <summary>Takes a place for one sample, waiting for room; false when <paramref name="token"/> stopped the wait.</summary>
    public bool Enter(CancellationToken token)
    {
        try
        {
            _room.Wait(token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The screen has shown a sample: its place is free.</summary>
    public void Leave() => _room.Release();
}
