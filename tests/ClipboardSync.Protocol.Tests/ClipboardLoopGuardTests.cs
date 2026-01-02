using ClipboardSync.Protocol;

namespace ClipboardSync.Protocol.Tests;

public sealed class ClipboardLoopGuardTests
{
    [Fact]
    public void ShouldIgnoreRemote_True_WhenSameDevice()
    {
        var id = Guid.NewGuid();
        Assert.True(ClipboardLoopGuard.ShouldIgnoreRemote(id, id));
    }

    [Fact]
    public void BeginRemoteApply_CausesShouldIgnoreLocalClipboardEvent_ForWindow()
    {
        var guard = new ClipboardLoopGuard(ignoreLocalAfterRemoteSetWindow: TimeSpan.FromMilliseconds(600));

        var t0 = DateTimeOffset.UtcNow;
        guard.BeginRemoteApply(t0);

        Assert.True(guard.ShouldIgnoreLocalClipboardEvent(t0.AddMilliseconds(100)));
        Assert.False(guard.ShouldIgnoreLocalClipboardEvent(t0.AddMilliseconds(800)));
    }

    [Fact]
    public void ShouldSuppressLocalPublish_SuppressesDuplicatesByHash()
    {
        var guard = new ClipboardLoopGuard(localDebounceWindow: TimeSpan.FromMilliseconds(0));

        var now = DateTimeOffset.UtcNow;
        var hash = ClipboardProtocol.ComputeTextHashUtf8("x");

        Assert.False(guard.ShouldSuppressLocalPublish(now, hash));          // first publish allowed
        Assert.True(guard.ShouldSuppressLocalPublish(now.AddSeconds(1), hash)); // duplicate suppressed
    }

    [Fact]
    public void ShouldSuppressLocalPublish_SuppressesDebouncedPublishes()
    {
        var guard = new ClipboardLoopGuard(localDebounceWindow: TimeSpan.FromMilliseconds(200));

        var now = DateTimeOffset.UtcNow;
        var hash1 = ClipboardProtocol.ComputeTextHashUtf8("a");
        var hash2 = ClipboardProtocol.ComputeTextHashUtf8("b");

        Assert.False(guard.ShouldSuppressLocalPublish(now, hash1));                // allowed
        Assert.True(guard.ShouldSuppressLocalPublish(now.AddMilliseconds(50), hash2)); // debounced
        Assert.False(guard.ShouldSuppressLocalPublish(now.AddMilliseconds(300), hash2)); // allowed
    }
}


