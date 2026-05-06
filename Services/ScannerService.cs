using FvCore.Data;
using FvCore.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace FvCore.Services;

public class ScannerService : IHostedService
{
    private readonly ILogger<ScannerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly GenreClassifierService _genreClassifier;
    private bool _isRunning = false;

    private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".aiff",
            ".mp4", ".mkv", ".avi", ".webm", ".3gp"
        };
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    public ScannerService(ILogger<ScannerService> logger, IServiceProvider serviceProvider, IConfiguration configuration, GenreClassifierService genreClassifier)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _genreClassifier = genreClassifier;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SyncAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SYNC INICIADO");
        if (_isRunning)
        {
            _logger.LogWarning("SYNC BLOQUEADO: ya hay uno en ejecución");
            return;
        }

        try
        {
            _isRunning = true;
            _logger.LogInformation("╔══════════════════════════════════════════╗");
            _logger.LogInformation("║   FV-CORE Scanner Service (Master Fix)   ║");
            _logger.LogInformation("╚══════════════════════════════════════════╝");
            
            var credPath = _configuration["GoogleDrive:CredentialsPath"];
            var rootFolderId = _configuration["GoogleDrive:FolderId"];

            _logger.LogInformation("CredPath: {Path}", credPath);
            _logger.LogInformation("CredFileExists: {Exists}", !string.IsNullOrEmpty(credPath) && File.Exists(credPath));
            _logger.LogInformation("FolderId: {Id}", rootFolderId);

            if (string.IsNullOrEmpty(credPath) || !File.Exists(credPath) || string.IsNullOrEmpty(rootFolderId))
            {
                _logger.LogError("Configuración de Drive inválida. Abortando.");
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FvCoreDbContext>();
            await EnsureDatabaseIntegrity(context, cancellationToken);

            _logger.LogInformation("Intentando autenticación con Google Drive...");
            var jsonCreds = await File.ReadAllTextAsync(credPath, cancellationToken);
            var credential = GoogleCredential.FromJson(jsonCreds).CreateScoped(DriveService.Scope.DriveReadonly);
            var driveService = new DriveService(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "FvCore Scanner" });

            _logger.LogInformation("Iniciando Escaneo Recursivo Total (DFS)...");
            
            var allEntries = new List<DriveEntry>();
            _logger.LogInformation("Antes de FetchRecursive...");
            await FetchRecursive(driveService, rootFolderId, new List<string>(), allEntries, cancellationToken);
            _logger.LogInformation("TOTAL FILES ENCONTRADOS: {Count}", allEntries.Count);

        var skipped = new List<string>();

        var mediaEntries = allEntries.Where(e =>
        {
            var valid = IsValidMedia(e.File);

            if (!valid)
                skipped.Add(e.File.Name);

            return valid;

        }).ToList();

        _logger.LogInformation(
            "FILES TOTAL: {Total} | MEDIA: {Media} | FILTRADOS: {Filtered}",
            allEntries.Count,
            mediaEntries.Count,
            skipped.Count
        );     
     
        if (mediaEntries.Count == 0)
        {
            _logger.LogWarning("SYNC VACÍO — NO SE TOCA DB");
            return;
        }

        var existingItems = await context.MediaItems
            .Where(m => m.GoogleDriveId != null)
            .ToDictionaryAsync(m => m.GoogleDriveId!, m => m, cancellationToken);
            
        var existingArtists = await context.Artists.ToDictionaryAsync(a => a.Nombre, a => a, StringComparer.OrdinalIgnoreCase, cancellationToken);

        int inserted = 0, updated = 0, batchCount = 0;

        foreach (var entry in mediaEntries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var file = entry.File;
            var pathParts = entry.PathParts; // Hierarchy excluding filename

            // Filter junk folders like CD1, Disc 2, etc.
            var cleanPaths = pathParts
                .Where(p => !Regex.IsMatch(p, @"^(cd|disc)\s*\d+$", RegexOptions.IgnoreCase))
                .ToList();

            string finalAlbum, finalArtist;
            if (cleanPaths.Count == 1)
            {
                finalArtist = cleanPaths[0];
                finalAlbum = "Singles";
            }
            else
            {
                finalAlbum = cleanPaths.LastOrDefault() ?? "Unknown";
                finalArtist = cleanPaths.Count >= 2 ? cleanPaths[cleanPaths.Count - 2] : "Unknown";
            }
            var finalTitle = string.IsNullOrWhiteSpace(file.Name) ? "Unknown" : CleanTrackTitle(Path.GetFileNameWithoutExtension(file.Name));

            _logger.LogInformation("[SYNC] Identified: {Title} | Artist: {Artist} | Album: {Album}", finalTitle, finalArtist, finalAlbum);
            
            bool isVideo = IsVideoFile(file);
            var type = isVideo ? MediaType.Video : MediaType.Audio;
                        
            if (existingItems.TryGetValue(file.Id, out var item))
            {
                if (!force && item.AlbumName == finalAlbum && item.ArtistName == finalArtist && item.Titulo == finalTitle) continue;

                item.Titulo = finalTitle;
                item.AlbumName = finalAlbum;
                item.ArtistName = finalArtist;
                updated++;
            }
            else
            {
                if (!existingArtists.TryGetValue(finalArtist, out var artistObj))
                {
                    artistObj = new Artist { Nombre = finalArtist };
                    context.Artists.Add(artistObj);
                    await context.SaveChangesAsync(cancellationToken); 
                    existingArtists[finalArtist] = artistObj;
                }

                var newItem = new MediaItem
                {
                    Titulo = finalTitle,
                    RutaArchivo = string.Join("/", pathParts) + "/" + file.Name,
                    GoogleDriveId = file.Id,
                    Tipo = type,
                    AlbumName = finalAlbum,
                    ArtistName = finalArtist,
                    ArtistId = artistObj.Id,
                    Artist = artistObj,
                    Genero = isVideo ? "Video" : _genreClassifier.GetGenreFromAI(file.Name, finalArtist, finalAlbum),
                    Calidad = type == MediaType.Audio ? GuessAudioQuality(file.Size) : GuessVideoQuality(file.Size),
                    IsFavorite = false
                };
                context.MediaItems.Add(newItem);
                inserted++;
            }           
            batchCount++;
            if (batchCount >= 200)
            {
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("[SYNC] Batch saved ({Count} items)...", batchCount);
                batchCount = 0;
            }
        }

        if (batchCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation("NUEVOS: {Inserted} | ACTUALIZADOS: {Updated}", inserted, updated);

        // ── Cover Art Pass ──────────────────────────────────────────────────
        // After all media items are saved, assign cover art to albums that don't
        // already have one, by looking for image files in the same Drive folders.
        await AssignAlbumCoversFromDrive(driveService, allEntries, context, cancellationToken);
    }
    finally
    {
        _isRunning = false;
        _logger.LogInformation("SYNC FINALIZADO");
    }
}

    /// <summary>
    /// Scans the already-fetched Drive entries for image files (cover.jpg, folder.jpg, etc.)
    /// and assigns their driveId as CoverPath to all MediaItems in the matching album.
    /// Skips albums that already have a valid CoverPath.
    /// </summary>
    private async Task AssignAlbumCoversFromDrive(
        DriveService driveService,
        List<DriveEntry> allEntries,
        FvCoreDbContext context,
        CancellationToken ct)
    {
        var coverNames = new[] { "cover.jpg", "folder.jpg", "album.jpg", "front.jpg", "artwork.jpg",
                                  "cover.jpeg", "folder.jpeg", "cover.png", "folder.png" };

        var imageEntries = allEntries
            .Where(e => ImageExtensions.Contains(
                Path.GetExtension(e.File.Name)?.ToLowerInvariant() ?? ""))
            .ToList();

        if (!imageEntries.Any())
        {
            _logger.LogInformation("[COVERS] No image files found in Drive scan.");
            return;
        }

        // Build lookup: folder-path-key (e.g. "Artist/Album") → best image entry
        var folderToImages = imageEntries
            .GroupBy(e => string.Join("/", e.PathParts))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e =>
                {
                    var name = e.File.Name.ToLowerInvariant();
                    var idx = Array.FindIndex(coverNames, c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return idx < 0 ? 9999 : idx;
                }).First());

        // Build lookup by last path segment (album folder name) as fallback
        var albumNameToImage = imageEntries
            .GroupBy(e => e.PathParts.LastOrDefault() ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e =>
                {
                    var name = e.File.Name.ToLowerInvariant();
                    var idx = Array.FindIndex(coverNames, c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return idx < 0 ? 9999 : idx;
                }).First(),
                StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("[COVERS] Image folders found: {Count}. Starting cover assignment.", folderToImages.Count);

        // FIX: Load items with NO valid cover — includes null, empty, and whitespace CoverPath.
        // Also re-process items whose CoverPath doesn't start with "driveId:" or "http"
        // (catches previously failed assignments).
        var nocover = await context.MediaItems
            .AsNoTracking()
            .Where(m => m.CoverPath == null
                     || m.CoverPath == ""
                     || m.CoverPath.Trim() == ""
                     || (!m.CoverPath.StartsWith("driveId:") && !m.CoverPath.StartsWith("http")))
            .Select(m => new { m.Id, m.AlbumName, m.ArtistName, m.RutaArchivo })
            .ToListAsync(ct);

        _logger.LogInformation("[COVERS] Items without valid cover: {Count}", nocover.Count);

        var updates = new Dictionary<int, string>();

        foreach (var item in nocover)
        {
            DriveEntry? bestImage = null;

            // Strategy 1: exact folder-path match derived from RutaArchivo
            var parts = item.RutaArchivo?.Split('/');
            if (parts != null && parts.Length >= 2)
            {
                var folderKey = string.Join("/", parts.Take(parts.Length - 1));
                if (folderToImages.TryGetValue(folderKey, out var img1))
                    bestImage = img1;
            }

            // Strategy 2: fallback — match by AlbumName against the last segment of image path
            if (bestImage == null && !string.IsNullOrWhiteSpace(item.AlbumName))
            {
                if (albumNameToImage.TryGetValue(item.AlbumName.Trim(), out var img2))
                    bestImage = img2;
            }

            if (bestImage != null)
                updates[item.Id] = $"driveId:{bestImage.File.Id}";
        }

        if (!updates.Any())
        {
            _logger.LogInformation("[COVERS] No new cover assignments needed.");
            return;
        }

        _logger.LogInformation("[COVERS] Assigning covers to {Count} media items...", updates.Count);

        var ids = updates.Keys.ToList();
        var items = await context.MediaItems
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct);

        foreach (var item in items)
        {
            if (updates.TryGetValue(item.Id, out var cover))
                item.CoverPath = cover;
        }

        await context.SaveChangesAsync(ct);
        _logger.LogInformation("[COVERS] Cover assignment complete: {Count} items updated.", items.Count);
    }


    private async Task FetchRecursive(DriveService service, string folderId, List<string> currentPath, List<DriveEntry> results, CancellationToken ct)
    {
        _logger.LogInformation("Escaneando carpeta: {Path}", string.Join("/", currentPath));
        
        string? pageToken = null;
        try 
        {
            do
            {
                var request = service.Files.List();
                request.Q = $"'{folderId}' in parents and trashed = false";
                request.Fields = "nextPageToken, files(id, name, mimeType, size)";
                request.PageToken = pageToken;
                request.PageSize = 1000;
                request.SupportsAllDrives = true;
                request.IncludeItemsFromAllDrives = true;

                var response = await request.ExecuteAsync(ct);
                if (response.Files != null)
                {
                    foreach (var file in response.Files)
                    {
                        if (file.MimeType == "application/vnd.google-apps.folder")
                        {
                            var newPath = new List<string>(currentPath) { file.Name };
                            await FetchRecursive(service, file.Id, newPath, results, ct);
                        }
                        else if (file.MimeType == "application/vnd.google-apps.shortcut")
                        {
                            var shortcutDetails = file.ShortcutDetails;

                            if (shortcutDetails?.TargetId != null)
                            {
                                var target = await service.Files.Get(shortcutDetails.TargetId).ExecuteAsync(ct);

                                if (target.MimeType == "application/vnd.google-apps.folder")
                                {
                                    var newPath = new List<string>(currentPath) { target.Name };
                                    await FetchRecursive(service, target.Id, newPath, results, ct);
                                }
                                else
                                {
                                    results.Add(new DriveEntry
                                    {
                                        File = target,
                                        PathParts = currentPath
                                    });
                                }
                            }
                        }
                        else
                        {
                            _logger.LogInformation(" - File: {Name} ({Size} bytes)", file.Name, file.Size);
                            results.Add(new DriveEntry { File = file, PathParts = currentPath });
                        }
                    }
                }
                pageToken = response.NextPageToken;
            } while (pageToken != null && !ct.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error en carpeta {Id}: {Msg}", folderId, ex.Message);
        }
    }

    private bool IsValidMedia(Google.Apis.Drive.v3.Data.File file)
    {
        if (string.IsNullOrWhiteSpace(file.Name)) return false;

        // Normalización con null-safety y manejo de edge cases
        var ext = Path.GetExtension(file.Name)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || ext == ".")
        {
            ext = null;
        }

        // 1. Prioridad: Filtro por extensión
        if (!string.IsNullOrEmpty(ext) && ValidExtensions.Contains(ext))
        {
            return true;
        }

        // 2. Fallback: MIME type SOLO si no hay extensión
        if (string.IsNullOrEmpty(ext))
        {
            if (file.MimeType?.StartsWith("audio/") == true || file.MimeType?.StartsWith("video/") == true)
            {
                return true;
            }
        }

        // 3. Log de archivos filtrados con contexto real
        _logger.LogWarning("[FILTERED] {Name} | Ext: {Ext} | Mime: {Mime}", file.Name, ext ?? "None", file.MimeType ?? "Unknown");
        return false;
    }

    private bool IsVideoFile(Google.Apis.Drive.v3.Data.File file)
    {
        var ext = Path.GetExtension(file.Name)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext) || ext == ".")
        {
            ext = null;
        }

        if (ext != null && (ext == ".mp4" || ext == ".webm" || ext == ".mkv" || ext == ".avi" || ext == ".3gp"))
        {
            return true;
        }

        return string.IsNullOrEmpty(ext) && file.MimeType?.StartsWith("video/") == true;
    }

    private class DriveEntry
    {
        public Google.Apis.Drive.v3.Data.File File { get; set; } = null!;
        public List<string> PathParts { get; set; } = new();
    }

    private async Task EnsureDatabaseIntegrity(FvCoreDbContext context, CancellationToken ct)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_MediaItems_GoogleDriveId_Unique') 
                BEGIN
                    CREATE UNIQUE NONCLUSTERED INDEX [IX_MediaItems_GoogleDriveId_Unique] 
                    ON [dbo].[MediaItems] ([GoogleDriveId]) WHERE [GoogleDriveId] IS NOT NULL;
                END
                
                IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MediaItems' AND COLUMN_NAME = 'ArtistName')
                BEGIN
                    ALTER TABLE MediaItems ADD ArtistName NVARCHAR(300) NULL;
                END
            ", ct);
        }
        catch { }
    }

    private static string CleanTrackTitle(string title)
    {
        title = Regex.Replace(title, @"^\d{1,2}\s*-\s*", "");
        title = Regex.Replace(title, @"\([^)]*\)", "").Trim();
        title = Regex.Replace(title, @"\[[^\]]*\]", "").Trim();
        return string.IsNullOrWhiteSpace(title) ? "Unknown" : title;
    }

    private static string GuessAudioQuality(long? sizeBytes)
    {
        if (sizeBytes == null) return "320kbps";
        var mb = sizeBytes.Value / 1024 / 1024;
        if (mb > 20) return "FLAC";
        if (mb > 10) return "320kbps";
        return "128kbps";
    }

    private static string GuessVideoQuality(long? sizeBytes)
    {
        if (sizeBytes == null) return "1080p";
        var mb = sizeBytes.Value / 1024 / 1024;
        if (mb > 1000) return "4K";
        if (mb > 300) return "1080p";
        return "720p";
    }
}
