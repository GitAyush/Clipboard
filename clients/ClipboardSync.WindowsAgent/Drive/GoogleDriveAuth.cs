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
    private static readonly string[] Scopes = { DriveService.Scope.DriveAppdata };

    public static async Task<DriveService> GetDriveServiceAsync(string clientSecretsPath, string tokenStorePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientSecretsPath))
            throw new InvalidOperationException("GoogleClientSecretsPath is required for Drive mode.");

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

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ClipboardSync"
        });
    }
}


