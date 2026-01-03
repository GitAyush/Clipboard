namespace ClipboardSync.WindowsAgent.Settings;

public sealed class AppSettings
{
    public string ServerBaseUrl { get; set; } = "http://localhost:5104";
    public string DeviceName { get; set; } = Environment.MachineName;
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public bool PublishLocalClipboard { get; set; } = true;

    /// <summary>
    /// When enabled, the agent will authenticate to RelayServer using the same Google account used for Drive mode.
    /// This allows devices logged into the same Google account to share clipboard sync without sharing a room secret.
    /// For local testing, you can keep this OFF and use the roomId+roomSecret flow.
    /// </summary>
    public bool UseGoogleAccountAuth { get; set; } = false;

    /// <summary>
    /// Max UTF-8 bytes allowed for inline text sync (default 64KB, optional 256KB).
    /// Text larger than this should use the "upload as file" flow (Drive mode / future).
    /// </summary>
    public int MaxInlineTextBytes { get; set; } = ClipboardSync.Protocol.ClipboardProtocol.DefaultMaxTextBytesUtf8;

    /// <summary>
    /// Max bytes allowed for explicit uploads (default 1MB), configurable up to 10MB.
    /// </summary>
    public int MaxUploadBytes { get; set; } = 1 * 1024 * 1024;

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


