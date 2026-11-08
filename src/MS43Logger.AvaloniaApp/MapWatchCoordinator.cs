using System;
using MS43Logger.Core.LiveTuning;

namespace MS43Logger.AvaloniaApp;

/// <summary>
/// One V9 watch for the whole app: the table of the cursor controller most recently brought to the front (a new table bound,
/// a map window focused, its tab shown). It sends the watch only when that changes, and none when no table is in front.
/// </summary>
public sealed class MapWatchCoordinator(Func<IWatchTarget?> target)
{
    private LiveCursorController? _front;
    private IWatchTarget? _sentTo;
    private uint? _sent;

    /// <summary>The controller whose table the watch follows; null when none is in front.</summary>
    public LiveCursorController? Front => _front;

    public void Activate(LiveCursorController controller)
    {
        _front = controller;
        Send();
    }

    /// <summary>The controller's table is no longer on screen: the watch stops unless another one comes to the front.</summary>
    public void Hide(LiveCursorController controller)
    {
        if (ReferenceEquals(_front, controller))
        {
            _front = null;
            Send();
        }
    }

    /// <summary>Sends the watch again after the line changed (a connect or a reconnect brings a new target).</summary>
    public void Send()
    {
        if (target() is not { } watch)
        {
            _sentTo = null;
            return;
        }

        var address = _front is { IsVisible: true } front ? front.WatchAddress : null;
        if (ReferenceEquals(watch, _sentTo) && address == _sent)
        {
            return;
        }

        watch.SetWatch(address);
        (_sentTo, _sent) = (watch, address);
    }
}
