namespace ClipboardSync.WindowsAgent;

/// <summary>
/// Simple process-wide flag so we can distinguish user-closing a window (hide instead)
/// vs app shutdown/restart (allow close).
/// </summary>
public static class AppExitState
{
    public static volatile bool IsExiting = false;
}


