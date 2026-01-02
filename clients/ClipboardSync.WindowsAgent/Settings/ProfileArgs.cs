namespace ClipboardSync.WindowsAgent.Settings;

public static class ProfileArgs
{
    /// <summary>
    /// Allows running multiple instances on the same machine for testing:
    /// - <c>--profile A</c> uses a different settings file (and therefore a different deviceId).
    /// </summary>
    public static string? TryGetProfileName(string[] args)
    {
        if (args is null || args.Length == 0) return null;

        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length) return null;
            var value = args[i + 1]?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}


