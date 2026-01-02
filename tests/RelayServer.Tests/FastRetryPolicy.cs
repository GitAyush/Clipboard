using Microsoft.AspNetCore.SignalR.Client;

namespace RelayServer.Tests;

internal sealed class FastRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan[] _delays;

    public FastRetryPolicy(params TimeSpan[] delays)
    {
        _delays = delays.Length == 0
            ? new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500) }
            : delays;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.PreviousRetryCount < 0) return TimeSpan.Zero;
        if (retryContext.PreviousRetryCount >= _delays.Length) return null;
        return _delays[retryContext.PreviousRetryCount];
    }
}


