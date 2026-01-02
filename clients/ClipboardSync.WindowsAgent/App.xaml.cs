using System.Diagnostics.CodeAnalysis;

namespace ClipboardSync.WindowsAgent;

[ExcludeFromCodeCoverage]
public sealed partial class App : System.Windows.Application
{
    private AppHost? _host;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
        _host = new AppHost();
        _host.Start(e.Args);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}


