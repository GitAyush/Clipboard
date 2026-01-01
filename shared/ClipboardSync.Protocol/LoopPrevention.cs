using System;
using System.Threading;

namespace ClipboardSync.Protocol;

/// <summary>
/// Phase 1 loop prevention rules (text-only, global group):
/// - Ignore remote updates where originDeviceId == local deviceId
/// - When applying a remote update to the local clipboard, enable a short-lived guard to ignore the resulting local clipboard event
/// - Debounce rapid local clipboard changes (typical 150–300ms)
/// - Optionally suppress resending the same content by tracking last published hash
/// </summary>
public sealed class ClipboardLoopGuard
{
    private readonly TimeSpan _ignoreLocalAfterRemoteSetWindow;
    private readonly TimeSpan _localDebounceWindow;

    private long _ignoreLocalUntilUtcTicks;
    private long _lastLocalPublishUtcTicks;
    private byte[]? _lastPublishedHash;

    public ClipboardLoopGuard(
        TimeSpan? ignoreLocalAfterRemoteSetWindow = null,
        TimeSpan? localDebounceWindow = null)
    {
        _ignoreLocalAfterRemoteSetWindow = ignoreLocalAfterRemoteSetWindow ?? TimeSpan.FromMilliseconds(600);
        _localDebounceWindow = localDebounceWindow ?? TimeSpan.FromMilliseconds(200);
    }

    /// <summary>
    /// Call immediately before setting the local clipboard from a remote update.
    /// </summary>
    public void BeginRemoteApply(DateTimeOffset nowUtc)
    {
        var until = nowUtc.Add(_ignoreLocalAfterRemoteSetWindow).UtcTicks;
        Interlocked.Exchange(ref _ignoreLocalUntilUtcTicks, until);
    }

    /// <summary>
    /// Returns true if a local clipboard event should be ignored due to a recent remote apply.
    /// </summary>
    public bool ShouldIgnoreLocalClipboardEvent(DateTimeOffset nowUtc)
    {
        var until = Interlocked.Read(ref _ignoreLocalUntilUtcTicks);
        return nowUtc.UtcTicks <= until;
    }

    /// <summary>
    /// Returns true if we should suppress publishing due to debounce or duplicate content.
    /// If false is returned, this method records the publish.
    /// </summary>
    public bool ShouldSuppressLocalPublish(DateTimeOffset nowUtc, byte[] currentHash)
    {
        if (currentHash is null) throw new ArgumentNullException(nameof(currentHash));

        // Debounce
        var lastTicks = Interlocked.Read(ref _lastLocalPublishUtcTicks);
        if (lastTicks != 0)
        {
            var since = nowUtc.UtcTicks - lastTicks;
            if (since >= 0 && since < _localDebounceWindow.Ticks)
            {
                return true;
            }
        }

        // Duplicate content
        var lastHash = Volatile.Read(ref _lastPublishedHash);
        if (lastHash is not null && ClipboardProtocol.HashEquals(lastHash, currentHash))
        {
            return true;
        }

        Interlocked.Exchange(ref _lastLocalPublishUtcTicks, nowUtc.UtcTicks);
        Volatile.Write(ref _lastPublishedHash, currentHash);
        return false;
    }

    /// <summary>
    /// Returns true if a received remote update should be ignored because it originated from this device.
    /// </summary>
    public static bool ShouldIgnoreRemote(Guid localDeviceId, Guid originDeviceId)
        => localDeviceId == originDeviceId;
}


