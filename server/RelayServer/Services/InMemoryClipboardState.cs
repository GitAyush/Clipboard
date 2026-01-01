using ClipboardSync.Protocol;

namespace RelayServer.Services;

/// <summary>
/// Phase 1: ephemeral in-memory clipboard state (latest item only).
/// Used to push the latest clipboard value to newly connected clients.
/// </summary>
public sealed class InMemoryClipboardState
{
    private readonly object _gate = new();
    private ClipboardChanged? _latest;

    public ClipboardChanged? GetLatest()
    {
        lock (_gate)
        {
            return _latest;
        }
    }

    public void SetLatest(ClipboardChanged changed)
    {
        lock (_gate)
        {
            _latest = changed;
        }
    }
}


