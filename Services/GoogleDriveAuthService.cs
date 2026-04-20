using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;

namespace FvCore.Services;

/// <summary>
/// Singleton service for providing Google Drive authentication tokens.
/// Caches the underlying credential to optimize stream operations.
/// </summary>
public class GoogleDriveAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleDriveAuthService> _logger;
    private readonly Lazy<GoogleCredential> _credential;

    public GoogleDriveAuthService(IConfiguration configuration, ILogger<GoogleDriveAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _credential = new Lazy<GoogleCredential>(LoadCredential);
    }

    private GoogleCredential LoadCredential()
    {
        var credPath = _configuration["GoogleDrive:CredentialsPath"];
        if (string.IsNullOrEmpty(credPath) || !File.Exists(credPath))
        {
            _logger.LogError("Google Drive credentials not found at {CredPath}.", credPath);
            throw new FileNotFoundException($"Google Drive credentials not found at {credPath}.");
        }

        var jsonCreds = File.ReadAllText(credPath);
        _logger.LogInformation("Loaded Google Drive credentials for streaming.");
        return GoogleCredential.FromJson(jsonCreds).CreateScoped(DriveService.Scope.DriveReadonly);
    }

    /// <summary>
    /// Gets a valid access token for API requests. The underlying Google SDK handles caching and refreshing automatically.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var credential = _credential.Value.UnderlyingCredential;
        return await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}
