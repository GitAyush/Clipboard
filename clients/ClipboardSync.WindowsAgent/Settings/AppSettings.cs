namespace ClipboardSync.WindowsAgent.Settings;

public sealed class AppSettings
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5104";
    public string DeviceName { get; set; } = Environment.MachineName;
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public bool PublishLocalClipboard { get; set; } = true;

    /// <summary>
    /// Sync mode:
    /// - Relay: server relays payload (legacy Phase 1)
    /// - Drive: clipboard payload goes to Google Drive; server only relays pointers/metadata
    /// </summary>
    public string SyncMode { get; set; } = "Relay";

    /// <summary>Room/group scoping for pointer broadcasts.</summary>
    public string RoomId { get; set; } = "default";

    /// <summary>
    /// Shared secret for joining the room. MVP storage is local settings file.
    /// </summary>
    public string RoomSecret { get; set; } = "change-me";

    /// <summary>
    /// Path to Google OAuth client secrets JSON (downloaded from Google Cloud Console).
    /// Required for Drive mode.
    /// </summary>
    public string GoogleClientSecretsPath { get; set; } = "";
}


