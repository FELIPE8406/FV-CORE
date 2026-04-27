using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    [HttpGet("Streaming/download-album/{trackId:int}")]
    public async Task<IActionResult> DownloadAlbum(int trackId)
    {
        try
        {
            var referenceTrack = await _context.MediaItems
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == trackId);

            if (referenceTrack == null || string.IsNullOrEmpty(referenceTrack.AlbumName))
                return Content("Error: Álbum no encontrado o la pista no pertenece a ningún álbum.");

            _context.Database.SetCommandTimeout(300);

            var tracksQuery = _context.MediaItems
                .AsNoTracking()
                .Where(m => m.AlbumName == referenceTrack.AlbumName);
            
            if (referenceTrack.ArtistId.HasValue)
                tracksQuery = tracksQuery.Where(m => m.ArtistId == referenceTrack.ArtistId);
            else if (!string.IsNullOrEmpty(referenceTrack.ArtistName))
                tracksQuery = tracksQuery.Where(m => m.ArtistName == referenceTrack.ArtistName);

            var tracks = await tracksQuery.ToListAsync();

            if (!tracks.Any())
                return Content("Error: El álbum tiene 0 pistas válidas.");

            var safeAlbumName = new string(referenceTrack.AlbumName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            safeAlbumName = System.Text.RegularExpressions.Regex.Replace(safeAlbumName, @"_+", "_").Trim('_');
            var zipName = $"{safeAlbumName}.zip";

            var syncIoFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
            if (syncIoFeature != null)
            {
                syncIoFeature.AllowSynchronousIO = true;
            }

            Response.ContentType = "application/zip";
            Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{zipName}\"");
            await Response.Body.FlushAsync();

            using (var archive = new System.IO.Compression.ZipArchive(Response.Body, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                try 
                {
                    var token = await _authService.GetAccessTokenAsync();
                    var httpClient = _httpClientFactory.CreateClient();

                    var downloadSemaphore = new SemaphoreSlim(4);
                    var archiveSemaphore = new SemaphoreSlim(1);

                    var downloadTasks = tracks.Where(t => !string.IsNullOrEmpty(t.GoogleDriveId)).Select(async track =>
                    {
                        await downloadSemaphore.WaitAsync();
                        try
                        {
                            var requestUrl = $"https://www.googleapis.com/drive/v3/files/{track.GoogleDriveId}?alt=media";
                            var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                            var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
                            if (response.IsSuccessStatusCode)
                            {
                                var ext = Path.GetExtension(track.RutaArchivo);
                                if (string.IsNullOrEmpty(ext)) ext = ".mp3";
                                
                                var safeFileName = new string(track.Titulo.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                                safeFileName = System.Text.RegularExpressions.Regex.Replace(safeFileName, @"_+", "_").Trim('_');
                                var entryName = $"{safeFileName}{ext}";
                                
                                var ms = new MemoryStream();
                                using (var bodyStream = await response.Content.ReadAsStreamAsync())
                                {
                                    await bodyStream.CopyToAsync(ms, 81920);
                                }
                                ms.Position = 0;

                                await archiveSemaphore.WaitAsync();
                                try
                                {
                                    var entry = archive.CreateEntry(entryName, System.IO.Compression.CompressionLevel.NoCompression);
                                    using var entryStream = entry.Open();
                                    await ms.CopyToAsync(entryStream, 81920);
                                    await Response.Body.FlushAsync();
                                }
                                finally
                                {
                                    archiveSemaphore.Release();
                                    ms.Dispose();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            await archiveSemaphore.WaitAsync();
                            try
                            {
                                var errorEntry = archive.CreateEntry($"error_{track.Id}.txt");
                                using var errStream = errorEntry.Open();
                                using var sw = new StreamWriter(errStream);
                                await sw.WriteAsync($"Error en pista {track.Id}: {ex.Message}");
                            }
                            finally
                            {
                                archiveSemaphore.Release();
                            }
                        }
                        finally
                        {
                            downloadSemaphore.Release();
                        }
                    });

                    await Task.WhenAll(downloadTasks);

                    var coverTrack = tracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.CoverPath));
                    if (coverTrack != null)
                    {
                        if (coverTrack.CoverPath.StartsWith("driveId:"))
                        {
                            var driveId = coverTrack.CoverPath.Substring(8);
                            var requestUrl = $"https://www.googleapis.com/drive/v3/files/{driveId}?alt=media";
                            var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                            httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                            var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
                            if (response.IsSuccessStatusCode)
                            {
                                var entry = archive.CreateEntry("cover.jpg", System.IO.Compression.CompressionLevel.NoCompression);
                                using var entryStream = entry.Open();
                                using var bodyStream = await response.Content.ReadAsStreamAsync();
                                await bodyStream.CopyToAsync(entryStream, 81920);
                            }
                        }
                        else if (System.IO.File.Exists(coverTrack.CoverPath))
                        {
                            var ext = Path.GetExtension(coverTrack.CoverPath);
                            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                            var entry = archive.CreateEntry($"cover{ext}", System.IO.Compression.CompressionLevel.NoCompression);
                            using var entryStream = entry.Open();
                            using var fileStream = System.IO.File.OpenRead(coverTrack.CoverPath);
                            await fileStream.CopyToAsync(entryStream, 81920);
                        }
                    }
                }
                catch (Exception archiveEx)
                {
                    _logger.LogError(archiveEx, "Error procesando el ZipArchive");
                    var errorEntry = archive.CreateEntry("ERROR.txt");
                    using var errStream = errorEntry.Open();
                    using var sw = new StreamWriter(errStream);
                    sw.Write($"Error interno generando el ZIP: {archiveEx.Message}");
                }
            }

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar el álbum completo");
            if (!Response.HasStarted)
            {
                return Content($"Error inesperado al generar el ZIP del álbum: {ex.Message}");
            }
            return new EmptyResult();
        }
    }
}
