using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Google.Apis.Drive.v3;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace ClipboardSync.WindowsAgent.Drive;

/// <summary>
/// Stores the per-room history manifest in Google Drive appDataFolder.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class DriveManifestStore : IDriveManifestStore
{
    private readonly DriveService _drive;

    public DriveManifestStore(DriveService drive)
    {
        _drive = drive;
    }

    private static string ManifestName(string roomId) => $"manifest.{roomId}.json";

    public async Task<DriveHistoryManifest> LoadAsync(string roomId, CancellationToken ct)
    {
        var name = ManifestName(roomId);
        var existing = await FindByNameAsync(name, ct);
        if (existing is null)
            return DriveHistoryManifest.CreateEmpty(roomId);

        var req = _drive.Files.Get(existing.Id);
        using var ms = new MemoryStream();
        await req.DownloadAsync(ms, ct);
        var json = Encoding.UTF8.GetString(ms.ToArray());

        try
        {
            var manifest = DriveHistoryManifest.Deserialize(json);
            if (string.IsNullOrWhiteSpace(manifest.RoomId)) manifest.RoomId = roomId;
            return manifest;
        }
        catch
        {
            // Corrupt manifest: start over.
            return DriveHistoryManifest.CreateEmpty(roomId);
        }
    }

    public async Task SaveAsync(DriveHistoryManifest manifest, CancellationToken ct)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(manifest.RoomId)) throw new ArgumentException("RoomId required.", nameof(manifest));

        var name = ManifestName(manifest.RoomId);
        var json = DriveHistoryManifest.Serialize(manifest);
        var bytes = Encoding.UTF8.GetBytes(json);

        var existing = await FindByNameAsync(name, ct);
        if (existing is null)
        {
            var meta = new DriveFile
            {
                Name = name,
                Parents = new List<string> { "appDataFolder" }
            };

            using var ms = new MemoryStream(bytes);
            var create = _drive.Files.Create(meta, ms, "application/json");
            create.Fields = "id";
            var res = await create.UploadAsync(ct);
            if (res.Status != Google.Apis.Upload.UploadStatus.Completed)
                throw new InvalidOperationException($"Drive manifest create failed: {res.Status} {res.Exception?.Message}");
            return;
        }
        else
        {
            var meta = new DriveFile { Name = name };
            using var ms = new MemoryStream(bytes);
            var update = _drive.Files.Update(meta, existing.Id, ms, "application/json");
            update.Fields = "id";
            var res = await update.UploadAsync(ct);
            if (res.Status != Google.Apis.Upload.UploadStatus.Completed)
                throw new InvalidOperationException($"Drive manifest update failed: {res.Status} {res.Exception?.Message}");
        }
    }

    private async Task<DriveFile?> FindByNameAsync(string name, CancellationToken ct)
    {
        var list = _drive.Files.List();
        list.Spaces = "appDataFolder";
        list.Fields = "files(id,name)";
        var safeName = name.Replace("'", "\\'");
        list.Q = $"name = '{safeName}' and trashed = false";

        var result = await list.ExecuteAsync(ct);
        return result.Files?.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
    }
}


