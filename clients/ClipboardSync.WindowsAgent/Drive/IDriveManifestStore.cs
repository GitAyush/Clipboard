namespace ClipboardSync.WindowsAgent.Drive;

public interface IDriveManifestStore
{
    Task<DriveHistoryManifest> LoadAsync(string roomId, CancellationToken ct);
    Task SaveAsync(DriveHistoryManifest manifest, CancellationToken ct);
}


