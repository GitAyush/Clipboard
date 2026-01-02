namespace ClipboardSync.WindowsAgent.Settings;

public sealed class AppSettings
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5104";
    public string DeviceName { get; set; } = Environment.MachineName;
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public bool PublishLocalClipboard { get; set; } = true;
}


