using System.Text.Json;
using ClipboardSync.Protocol;

namespace ClipboardSync.WindowsAgent.Drive;

/// <summary>
/// Drive mode remote history manifest stored in Google Drive appDataFolder.
/// Contains metadata + pointers needed to download items later.
/// </summary>
public sealed class DriveHistoryManifest
{
    public string RoomId { get; set; } = "";
    public List<DriveHistoryManifestItem> Items { get; set; } = new();

    public static DriveHistoryManifest CreateEmpty(string roomId) => new()
    {
        RoomId = roomId,
        Items = new List<DriveHistoryManifestItem>()
    };

    public void AppendOrReplaceNewestFirst(DriveHistoryManifestItem item, int maxItems)
    {
        maxItems = Math.Clamp(maxItems, 1, 200);

        // Remove any existing matching id, then insert at front.
        Items.RemoveAll(x => string.Equals(x.Id, item.Id, StringComparison.Ordinal));
        Items.Insert(0, item);

        if (Items.Count > maxItems)
            Items.RemoveRange(maxItems, Items.Count - maxItems);
    }

    public HistoryItem[] ToHistoryItems()
        => Items.Select(x => x.ToHistoryItem(RoomId)).ToArray();

    public static string Serialize(DriveHistoryManifest manifest)
        => JsonSerializer.Serialize(manifest, JsonOptions);

    public static DriveHistoryManifest Deserialize(string json)
        => JsonSerializer.Deserialize<DriveHistoryManifest>(json, JsonOptions)
           ?? throw new InvalidOperationException("Manifest JSON invalid/empty.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}

public sealed class DriveHistoryManifestItem
{
    public string Id { get; set; } = "";
    public int Kind { get; set; } // 0=text, 1=file
    public string OriginDeviceId { get; set; } = "";
    public long TsUtcMs { get; set; }
    public string Title { get; set; } = "";
    public string? Preview { get; set; }
    public long SizeBytes { get; set; }
    public string ContentType { get; set; } = "";
    public string ContentHashBase64 { get; set; } = "";
    public string? ProviderFileId { get; set; }
    public string? ObjectKey { get; set; }

    public static DriveHistoryManifestItem FromPointer(ClipboardItemPointer p, HistoryItemKind kind, string title, string? preview)
    {
        return new DriveHistoryManifestItem
        {
            Id = $"{p.TsUtcMs}_{p.OriginDeviceId:N}_{ClipboardProtocol.HashToHex(p.ContentHash)}",
            Kind = (int)kind,
            OriginDeviceId = p.OriginDeviceId.ToString(),
            TsUtcMs = p.TsUtcMs,
            Title = title,
            Preview = preview,
            SizeBytes = p.SizeBytes,
            ContentType = p.ContentType,
            ContentHashBase64 = Convert.ToBase64String(p.ContentHash),
            ProviderFileId = p.ProviderFileId,
            ObjectKey = p.ObjectKey
        };
    }

    public HistoryItem ToHistoryItem(string roomId)
    {
        var kind = (HistoryItemKind)Kind;
        var origin = Guid.TryParse(OriginDeviceId, out var g) ? g : Guid.Empty;
        var hash = string.IsNullOrWhiteSpace(ContentHashBase64) ? Array.Empty<byte>() : Convert.FromBase64String(ContentHashBase64);

        return new HistoryItem(
            Id: Id,
            RoomId: roomId,
            Kind: kind,
            OriginDeviceId: origin,
            TsUtcMs: TsUtcMs,
            Title: Title,
            Preview: Preview,
            SizeBytes: SizeBytes,
            ContentHash: hash,
            ContentType: ContentType,
            ProviderFileId: ProviderFileId,
            ObjectKey: ObjectKey
        );
    }
}


