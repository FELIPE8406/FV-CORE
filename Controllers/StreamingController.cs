using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using FvCore.Services;
using Google.Apis.Auth.OAuth2;
using System.Diagnostics;

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
        
        var ext = Path.GetExtension(media.RutaArchivo)?.ToLowerInvariant() ?? "";
        return await StreamDriveFile(media.GoogleDriveId, ext);
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
        
        var requestUrl = $"https://www.googleapis.com/drive/v3/files/{media.GoogleDriveId}?alt=media&supportsAllDrives=true";
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
                            var requestUrl = $"https://www.googleapis.com/drive/v3/files/{track.GoogleDriveId}?alt=media&supportsAllDrives=true";
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
                            var requestUrl = $"https://www.googleapis.com/drive/v3/files/{driveId}?alt=media&supportsAllDrives=true";
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

    // ─────────────────────────────────────────────────────────────────────────
    // VIDEO STREAM WITH ON-THE-FLY TRANSCODING (AVI / MKV → MP4 H.264)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Streams any video from Google Drive, transcoding it on-the-fly to H.264/AAC MP4
    /// via FFmpeg when the source format is not natively supported by browsers (e.g. .avi, .mkv).
    /// Falls back to a direct stream if FFmpeg is unavailable on the server.
    /// </summary>
    [HttpGet("Streaming/video/{id:int}")]
    public async Task<IActionResult> VideoStream(int id)
    {
        var media = await _context.MediaItems.FindAsync(id);
        if (media == null || string.IsNullOrEmpty(media.GoogleDriveId))
            return NotFound("Media not found or not synced with Google Drive.");

        var ext = Path.GetExtension(media.RutaArchivo)?.ToLowerInvariant();

        // Browser-native formats: pass through directly without transcoding
        var nativeFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".ogg" };

        if (nativeFormats.Contains(ext ?? ""))
        {
            // Re-use the standard stream proxy logic
            return await StreamDriveFile(media.GoogleDriveId, ext!);
        }

        // Non-native format (e.g. .avi, .mkv) — try FFmpeg transcoding
        var ffmpegPath = FindFfmpeg();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            _logger.LogWarning("FFmpeg not found on server. Falling back to direct stream for {Ext} video (may not play in browser).", ext);
            return await StreamDriveFile(media.GoogleDriveId, ext ?? ".avi");
        }

        _logger.LogInformation("Transcoding {Title} ({Ext}) via FFmpeg at {FfmpegPath}", media.Titulo, ext, ffmpegPath);

        // Fetch the raw bytes from Google Drive into a pipe via FFmpeg
        var token = await _authService.GetAccessTokenAsync();
        var driveUrl = $"https://www.googleapis.com/drive/v3/files/{media.GoogleDriveId}?alt=media";

        Response.ContentType = "video/mp4";
        Response.Headers.Append("Cache-Control", "no-cache");
        // Content-Disposition inline so the browser plays it
        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{Path.GetFileNameWithoutExtension(media.RutaArchivo)}.mp4\"");

        // FFmpeg: read from stdin (piped from Drive), transcode, write MP4 to stdout
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            // -i pipe:0   → read input from stdin
            // -c:v libx264 -preset ultrafast -crf 23  → fast H.264 encode
            // -c:a aac -b:a 128k                       → AAC audio
            // -movflags frag_keyframe+empty_moov+faststart → streaming-compatible MP4
            // -f mp4 pipe:1                             → write MP4 to stdout
            Arguments = "-i pipe:0 -c:v libx264 -preset ultrafast -crf 23 -c:a aac -b:a 128k -movflags frag_keyframe+empty_moov+faststart -f mp4 pipe:1",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process? ffmpeg = null;
        try
        {
            ffmpeg = Process.Start(psi)!;

            // Pipe Drive download → FFmpeg stdin in background
            var driveTask = Task.Run(async () =>
            {
                try
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var req = new HttpRequestMessage(HttpMethod.Get, driveUrl);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var resp = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var driveStream = await resp.Content.ReadAsStreamAsync();
                        await driveStream.CopyToAsync(ffmpeg.StandardInput.BaseStream);
                    }
                }
                catch { /* Drive pipe may close when client disconnects */ }
                finally
                {
                    try { ffmpeg.StandardInput.Close(); } catch { }
                }
            });

            // Pipe FFmpeg stdout → HTTP response
            await ffmpeg.StandardOutput.BaseStream.CopyToAsync(Response.Body);
            await driveTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg transcoding error for media {Id}", id);
        }
        finally
        {
            try { ffmpeg?.Kill(); } catch { }
            try { ffmpeg?.Dispose(); } catch { }
        }

        return new EmptyResult();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INDIVIDUAL FILE DOWNLOAD (any track or video, original format)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Forces a browser download of a single media file in its original format.
    /// Preserves the album ZIP download flow untouched.
    /// </summary>
    [HttpGet("Streaming/download-file/{id:int}")]
    public async Task<IActionResult> DownloadFile(int id)
    {
        _logger.LogInformation("Single-file download requested for Media ID: {Id}", id);

        var media = await _context.MediaItems.FindAsync(id);
        if (media == null || string.IsNullOrEmpty(media.GoogleDriveId))
        {
            _logger.LogWarning("DownloadFile: Media {Id} not found or no Google Drive ID.", id);
            return NotFound("Media not found or not synced with Google Drive.");
        }

        // FIX: Force token refresh on each download to avoid stale singleton token.
        // Also add supportsAllDrives=true — required for Shared/Team Drive files (403 fix).
        var token = await _authService.GetAccessTokenAsync();
        var requestUrl = $"https://www.googleapis.com/drive/v3/files/{media.GoogleDriveId}?alt=media&supportsAllDrives=true";
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("DownloadFile: Drive API returned {Status} for Media {Id}. Error: {Err}", response.StatusCode, id, errContent);
            return StatusCode((int)response.StatusCode, $"Error fetching from Google Drive: {response.ReasonPhrase}. Details: {errContent}");
        }

        var ext = Path.GetExtension(media.RutaArchivo)?.ToLowerInvariant();
        var contentType = ext switch
        {
            ".mp3"  => "audio/mpeg",
            ".m4a"  => "audio/mp4",
            ".mp4"  => "video/mp4",
            ".webm" => "video/webm",
            ".ogg"  => "audio/ogg",
            ".wav"  => "audio/wav",
            ".flac" => "audio/flac",
            ".avi"  => "video/x-msvideo",
            ".mkv"  => "video/x-matroska",
            _ => response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream"
        };

        var invalidChars = Path.GetInvalidFileNameChars();
        var safeFileName = new string(media.Titulo.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        safeFileName = System.Text.RegularExpressions.Regex.Replace(safeFileName, @"_+", "_").Trim('_');
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        var finalName = $"{safeFileName}{ext}";

        _logger.LogInformation("Starting single-file download stream: {FinalName}", finalName);

        var bodyStream = await response.Content.ReadAsStreamAsync();

        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{finalName}\"");

        return new FileStreamResult(bodyStream, contentType)
        {
            FileDownloadName = finalName,
            EnableRangeProcessing = true
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IActionResult> StreamDriveFile(string driveId, string ext)
    {
        var token = await _authService.GetAccessTokenAsync();
        // FIX: supportsAllDrives=true required for Shared Drive files.
        var requestUrl = $"https://www.googleapis.com/drive/v3/files/{driveId}?alt=media&supportsAllDrives=true";
        var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Forward Range header so Drive returns 206 Partial Content — required for HTML5 seeking.
        if (Request.Headers.TryGetValue("Range", out var range))
            httpRequest.Headers.Add("Range", range.ToString());

        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("StreamDriveFile: Drive API error {Status}: {Err}", response.StatusCode, err);
            return StatusCode((int)response.StatusCode, $"Error fetching from Google Drive: {response.ReasonPhrase}. Details: {err}");
        }

        // Forward all content headers (Content-Length, Content-Range, etc.)
        foreach (var header in response.Content.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();

        // Forward Accept-Ranges from response headers
        if (response.Headers.TryGetValues("Accept-Ranges", out var acceptRanges))
            Response.Headers["Accept-Ranges"] = acceptRanges.ToArray();
        else
            Response.Headers["Accept-Ranges"] = "bytes";

        Response.Headers.Remove("Content-Type");
        
        var contentType = ext switch
        {
            ".mp3"  => "audio/mpeg",
            ".m4a"  => "audio/mp4",
            ".mp4"  => "video/mp4",
            ".webm" => "video/webm",
            ".ogg"  => "audio/ogg",
            ".wav"  => "audio/wav",
            ".flac" => "audio/flac",
            ".avi"  => "video/x-msvideo",
            ".mkv"  => "video/x-matroska",
            _ => response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream"
        };
        Response.ContentType = contentType;

        // Preserve exact status code (200 or 206)
        Response.StatusCode = (int)response.StatusCode;

        // Stream body directly to avoid FileStreamResult stripping headers or overriding status
        var bodyStream = await response.Content.ReadAsStreamAsync();
        await bodyStream.CopyToAsync(Response.Body);
        return new EmptyResult();
    }

    // FIX: Cache FFmpeg detection — evaluated ONCE at startup, not per request.
    // Previously FindFfmpeg() blocked up to 10s per video request (5 candidates × 2s WaitForExit),
    // causing the player to freeze at 0:00 while the server was busy probing for ffmpeg.
    private static readonly Lazy<string?> _ffmpegPath = new(DetectFfmpeg, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    private static string? DetectFfmpeg()
    {
        var candidates = new[]
        {
            "ffmpeg",
            "/usr/bin/ffmpeg",
            "/usr/local/bin/ffmpeg",
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe"
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(3000);
                    if (p.ExitCode == 0) return candidate;
                }
            }
            catch { /* candidate not available */ }
        }
        return null;
    }

    // Thin wrapper kept for readability at call sites
    private static string? FindFfmpeg() => _ffmpegPath.Value;
}

