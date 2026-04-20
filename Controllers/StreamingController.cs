using Microsoft.AspNetCore.Mvc;
using FvCore.Data;
using FvCore.Services;
using Google.Apis.Auth.OAuth2;

namespace FvCore.Controllers;

/// <summary>
/// Streams media files directly from Google Drive with HTTP Range Processing support for seeking.
/// </summary>
public class StreamingController : Controller
{
    private readonly FvCoreDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleDriveAuthService _authService;
    private readonly ILogger<StreamingController> _logger;

    public StreamingController(FvCoreDbContext context, IHttpClientFactory httpClientFactory, GoogleDriveAuthService authService, ILogger<StreamingController> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _authService = authService;
        _logger = logger;
    }

    [HttpGet("stream/{id:int}")]
    public async Task<IActionResult> Stream(int id)
    {
        var media = await _context.MediaItems.FindAsync(id);
        if (media == null || string.IsNullOrEmpty(media.GoogleDriveId))
            return NotFound("Media not found or not synced with Google Drive.");

        var token = await _authService.GetAccessTokenAsync();
        
        var requestUrl = $"https://www.googleapis.com/drive/v3/files/{media.GoogleDriveId}?alt=media";
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Forward Range header if present
        if (Request.Headers.TryGetValue("Range", out var range))
        {
            httpRequest.Headers.Add("Range", range.ToString());
        }

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, $"Error fetching from Google Drive: {response.ReasonPhrase}");
        }

        // Forward Content response headers (needed for range processing)
        foreach (var header in response.Content.Headers)
        {
            Response.Headers[header.Key] = header.Value.ToArray();
        }

        var bodyStream = await response.Content.ReadAsStreamAsync();
        var ext = Path.GetExtension(media.RutaArchivo)?.ToLowerInvariant();
        var contentType = ext switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            _ => response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream"
        };
        
        // Remove Content-Type from headers if present, we'll set it explicitly
        Response.Headers.Remove("Content-Type");

        // Provide the precise status code (206 if Range request, else 200)
        Response.StatusCode = (int)response.StatusCode;
        
        // We set enableRangeProcessing to false as we are already handling the Range forwarding
        return new FileStreamResult(bodyStream, contentType)
        {
            EnableRangeProcessing = false 
        };
    }

    [HttpGet("Streaming/download/{id:int}")]
    public async Task<IActionResult> Download(int id)
    {
        _logger.LogInformation("Download requested for Media ID: {Id}", id);
        var media = await _context.MediaItems.FindAsync(id);
        if (media == null || string.IsNullOrEmpty(media.GoogleDriveId))
        {
            _logger.LogWarning("Download failed: Media {Id} not found or no Google Drive ID.", id);
            return NotFound("Media not found or not synced with Google Drive.");
        }

        var token = await _authService.GetAccessTokenAsync();
        
        var requestUrl = $"https://www.googleapis.com/drive/v3/files/{media.GoogleDriveId}?alt=media";
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Download failed: Google Drive API returned {StatusCode} for Media {Id}", response.StatusCode, id);
            return StatusCode((int)response.StatusCode, $"Error fetching from Google Drive: {response.ReasonPhrase}");
        }

        var bodyStream = await response.Content.ReadAsStreamAsync();
        var ext = Path.GetExtension(media.RutaArchivo)?.ToLowerInvariant();
        var contentType = ext switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            _ => response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream"
        };
        
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeFileName = new string(media.Titulo.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        safeFileName = System.Text.RegularExpressions.Regex.Replace(safeFileName, @"_+", "_").Trim('_');
        if (string.IsNullOrEmpty(ext)) ext = ".xyz";
        
        var finalName = $"{safeFileName}{ext}";
        _logger.LogInformation("Starting download stream for {FinalName} from Google Drive", finalName);

        // Force attachment explicitly using headers as well (FileStreamResult will also do it)
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{finalName}\"");

        return new FileStreamResult(bodyStream, contentType)
        {
            FileDownloadName = finalName,
            EnableRangeProcessing = true
        };
    }
}
