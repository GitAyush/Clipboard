using System.Diagnostics;

namespace ClipboardSync.WindowsAgent.Tray;

internal static class RestartHelper
{
    public static ProcessStartInfo CreateRestartStartInfo()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            throw new InvalidOperationException("ProcessPath is unavailable.");

        var args = Environment.GetCommandLineArgs();
        var argString = args.Length <= 1 ? "" : string.Join(" ", args.Skip(1).Select(QuoteArg));

        return new ProcessStartInfo
        {
            FileName = exe,
            Arguments = argString,
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
    }

    public static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.Any(char.IsWhiteSpace) || arg.Contains('"'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        return arg;
    }
}


