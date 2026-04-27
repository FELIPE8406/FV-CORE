using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using Google.Apis.Auth.OAuth2;

namespace FvCore.Controllers;

/// <summary>
/// Serves cover images from local filesystem or Google Drive, or returns default placeholder.
/// </summary>
public class CoverController : Controller
{
    private readonly FvCoreDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CoverController(FvCoreDbContext context, IWebHostEnvironment env, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _context = context;
        _env = env;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet("cover/{id:int}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCover(int id)
    {
        var media = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.CoverPath, m.AlbumName })
            .FirstOrDefaultAsync();

        if (media == null) return DefaultPlaceholder();

        var coverPath = media.CoverPath;

        bool IsValidCover(string path) => !string.IsNullOrWhiteSpace(path) && 
            (path.StartsWith("driveId:") || path.StartsWith("http://") || path.StartsWith("https://") || System.IO.File.Exists(path));

        if (!IsValidCover(coverPath) && !string.IsNullOrWhiteSpace(media.AlbumName))
        {
            var albumCovers = await _context.MediaItems
                .AsNoTracking()
                .Where(m => m.AlbumName == media.AlbumName && m.CoverPath != null && m.CoverPath != "")
                .Select(m => m.CoverPath)
                .ToListAsync();

            coverPath = albumCovers.FirstOrDefault(c => IsValidCover(c));
        }

        if (coverPath != null)
        {
            if (coverPath.StartsWith("http://") || coverPath.StartsWith("https://"))
            {
                return Redirect(coverPath);
            }
            if (coverPath.StartsWith("driveId:"))
            {
                return await ProxyDriveFile(coverPath.Substring(8));
            }
            if (System.IO.File.Exists(coverPath))
            {
                var ext = Path.GetExtension(coverPath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".bmp" => "image/bmp",
                    ".webp" => "image/webp",
                    _ => "image/jpeg"
                };
                return PhysicalFile(coverPath, contentType);
            }
        }

        return DefaultPlaceholder();
    }

    [HttpGet("cover/artist/{id:int}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetArtistCover(int id)
    {
        var fotoUrl = await _context.Artists
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.FotoUrl)
            .FirstOrDefaultAsync();

        if (fotoUrl != null)
        {
            if (fotoUrl.StartsWith("driveId:"))
            {
                return await ProxyDriveFile(fotoUrl.Substring(8));
            }
            if (System.IO.File.Exists(fotoUrl))
            {
                var ext = Path.GetExtension(fotoUrl).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    ".bmp" => "image/bmp",
                    _ => "image/jpeg"
                };
                return PhysicalFile(fotoUrl, contentType);
            }
        }

        return DefaultPlaceholder();
    }

    private async Task<IActionResult> ProxyDriveFile(string driveId)
    {
        var credPath = _configuration["GoogleDrive:CredentialsPath"];
        if (string.IsNullOrEmpty(credPath) || !System.IO.File.Exists(credPath))
            return DefaultPlaceholder();

        GoogleCredential credential;
        using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream).CreateScoped(Google.Apis.Drive.v3.DriveService.Scope.DriveReadonly);
        }

        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        
        var requestUrl = $"https://www.googleapis.com/drive/v3/files/{driveId}?alt=media";
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
            return DefaultPlaceholder();

        var bodyStream = await response.Content.ReadAsStreamAsync();
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";

        return new FileStreamResult(bodyStream, contentType);
    }

    private IActionResult DefaultPlaceholder()
    {
        var defaultPath = Path.Combine(_env.WebRootPath, "images", "default-cover.svg");
        return PhysicalFile(defaultPath, "image/svg+xml");
    }
}
