using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Google.Apis.Drive.v3;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace ClipboardSync.WindowsAgent.Drive;

public interface IDriveClipboardStore
{
    Task<(string fileId, long sizeBytes)> UploadTextAsync(string objectKey, string text, CancellationToken ct);
    Task<string> DownloadTextAsync(string fileId, CancellationToken ct);
    Task<(string fileId, long sizeBytes)> UploadFileAsync(string objectKey, string fileName, Stream content, string contentType, CancellationToken ct);
    Task DownloadFileToPathAsync(string fileId, string path, CancellationToken ct);
}

[ExcludeFromCodeCoverage]
public sealed class DriveClipboardStore : IDriveClipboardStore
{
    private readonly DriveService _drive;

    public DriveClipboardStore(DriveService drive)
    {
        _drive = drive;
    }

    public async Task<(string fileId, long sizeBytes)> UploadTextAsync(string objectKey, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) throw new ArgumentException("objectKey is required.", nameof(objectKey));
        if (text is null) throw new ArgumentNullException(nameof(text));

        var bytes = Encoding.UTF8.GetBytes(text);

        var fileMetadata = new DriveFile
        {
            Name = Path.GetFileName(objectKey),
            Parents = new List<string> { "appDataFolder" },
            Description = objectKey
        };

        using var ms = new MemoryStream(bytes);
        var req = _drive.Files.Create(fileMetadata, ms, "text/plain");
        req.Fields = "id,size";
        req.SupportsAllDrives = false;

        var created = await req.UploadAsync(ct);
        if (created.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw new InvalidOperationException($"Drive upload failed: {created.Status} {created.Exception?.Message}");

        var file = req.ResponseBody ?? throw new InvalidOperationException("Drive upload returned no file metadata.");
        var id = file.Id ?? throw new InvalidOperationException("Drive upload returned empty file id.");

        long size = file.Size ?? bytes.Length;
        return (id, size);
    }

    public async Task<string> DownloadTextAsync(string fileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("fileId is required.", nameof(fileId));

        var req = _drive.Files.Get(fileId);
        using var ms = new MemoryStream();
        await req.DownloadAsync(ms, ct);
        var bytes = ms.ToArray();
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task<(string fileId, long sizeBytes)> UploadFileAsync(string objectKey, string fileName, Stream content, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(objectKey)) throw new ArgumentException("objectKey is required.", nameof(objectKey));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required.", nameof(fileName));
        if (content is null) throw new ArgumentNullException(nameof(content));
        if (string.IsNullOrWhiteSpace(contentType)) contentType = "application/octet-stream";

        var fileMetadata = new DriveFile
        {
            Name = fileName,
            Parents = new List<string> { "appDataFolder" },
            Description = objectKey
        };

        var req = _drive.Files.Create(fileMetadata, content, contentType);
        req.Fields = "id,size";
        req.SupportsAllDrives = false;

        var created = await req.UploadAsync(ct);
        if (created.Status != Google.Apis.Upload.UploadStatus.Completed)
            throw new InvalidOperationException($"Drive upload failed: {created.Status} {created.Exception?.Message}");

        var file = req.ResponseBody ?? throw new InvalidOperationException("Drive upload returned no file metadata.");
        var id = file.Id ?? throw new InvalidOperationException("Drive upload returned empty file id.");

        long size = file.Size ?? 0;
        return (id, size);
    }

    public async Task DownloadFileToPathAsync(string fileId, string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileId)) throw new ArgumentException("fileId is required.", nameof(fileId));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.", nameof(path));

        var req = _drive.Files.Get(fileId);
        await using var fs = System.IO.File.Create(path);
        await req.DownloadAsync(fs, ct);
    }
}


