using System.IO;
using System.Diagnostics.CodeAnalysis;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace ClipboardSync.WindowsAgent.Drive;

[ExcludeFromCodeCoverage]
public static class GoogleDriveAuth
{
    /// <summary>
    /// Scopes requested by ClipboardSync in Drive mode.
    /// - DriveAppdata: upload/download clipboard payloads to the user's Google Drive appDataFolder.
    /// - userinfo.email: used to display which Google account is logged in (optional, for transparency) and for server-side account scoping.
    /// - openid: enables identity tokens in some flows; also makes account identity explicit in consent screens.
    /// </summary>
    private static readonly string[] Scopes =
    {
        DriveService.Scope.DriveAppdata,
        "https://www.googleapis.com/auth/userinfo.email",
        "openid"
    };

    public static async Task<(DriveService drive, UserCredential credential, string resolvedClientSecretsPath)> GetDriveServiceAndCredentialAsync(
        string clientSecretsPath,
        string tokenStorePath,
        CancellationToken ct)
    {
        // Client secrets are "app credentials" (OAuth client_id) required to start the installed-app consent flow.
        // For convenience, allow omitting the path if a bundled client_secret*.json exists next to the executable.
        if (string.IsNullOrWhiteSpace(clientSecretsPath))
        {
            clientSecretsPath = TryFindBundledClientSecretsPath()
                ?? throw new InvalidOperationException(
                    "GoogleClientSecretsPath is missing and no bundled client_secret*.json was found next to the app. " +
                    "Provide a Google OAuth Desktop app client JSON in Settings → 'Google secrets path'.");
        }

        if (!File.Exists(clientSecretsPath))
            throw new FileNotFoundException("Google OAuth client secrets file not found.", clientSecretsPath);

        Directory.CreateDirectory(tokenStorePath);

        using var stream = new FileStream(clientSecretsPath, FileMode.Open, FileAccess.Read);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;

        // This will launch a browser-based consent flow for installed apps.
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            user: "default",
            taskCancellationToken: ct,
            dataStore: new FileDataStore(tokenStorePath, fullPath: true));

        var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ClipboardSync"
        });

        return (drive, credential, clientSecretsPath);
    }

    public static async Task<DriveService> GetDriveServiceAsync(string clientSecretsPath, string tokenStorePath, CancellationToken ct)
    {
        var (drive, _, _) = await GetDriveServiceAndCredentialAsync(clientSecretsPath, tokenStorePath, ct);
        return drive;
    }

    private static string? TryFindBundledClientSecretsPath()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;

            // Support either a friendly name or the standard Google download name pattern.
            var direct = Path.Combine(dir, "client_secret.json");
            if (File.Exists(direct)) return direct;

            var matches = Directory.GetFiles(dir, "client_secret*.json", SearchOption.TopDirectoryOnly);
            return matches.Length > 0 ? matches[0] : null;
        }
        catch
        {
            return null;
        }
    }
}


