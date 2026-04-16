using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using FvCore.Models;

namespace FvCore.Services;

/// <summary>
/// Background service that scans the multimedia directory on startup
/// and indexes all media files into the database.
/// Idempotent: checks RutaArchivo before inserting to avoid duplicates.
/// </summary>
public class ScannerService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScannerService> _logger;
    private readonly GenreClassifierService _genreClassifier;

    // Root media path
    private const string MediaRoot = @"C:\Users\Usuario\Documents\Felipe Valbuena\Multimedia";

    // Supported file extensions
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".wma", ".flac", ".ogg", ".wav", ".aac"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mkv", ".flv", ".wmv", ".mov", ".webm"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp"
    };

    // Cover file name patterns (priority order)
    private static readonly string[] CoverFileNames =
    {
        "cover.jpg", "cover.jpeg", "cover.png",
        "folder.jpg", "folder.jpeg", "folder.png",
        "front.jpg", "front.jpeg", "front.png",
        "album.jpg", "album.jpeg", "album.png",
        "albumart.jpg", "albumartsmall.jpg"
    };

    public ScannerService(IServiceProvider serviceProvider, ILogger<ScannerService> logger, GenreClassifierService genreClassifier)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _genreClassifier = genreClassifier;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("╔══════════════════════════════════════════╗");
        _logger.LogInformation("║   FV-CORE Scanner Service Starting...   ║");
        _logger.LogInformation("╚══════════════════════════════════════════╝");
        _logger.LogInformation("Scanning: {Path}", MediaRoot);

        if (!Directory.Exists(MediaRoot))
        {
            _logger.LogError("Media root directory not found: {Path}", MediaRoot);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FvCoreDbContext>();

        // Ensure database is created
        await context.Database.MigrateAsync(cancellationToken);

        // Get existing paths to avoid duplicates
        var existingPathsList = await context.MediaItems
            .Select(m => m.RutaArchivo)
            .ToListAsync(cancellationToken);
        var existingPaths = new HashSet<string>(existingPathsList);

        var existingArtists = await context.Artists
            .ToDictionaryAsync(a => a.Nombre, a => a, StringComparer.OrdinalIgnoreCase, cancellationToken);

        int newTracks = 0;
        int newArtists = 0;
        int newVideos = 0;

        var topLevelDirs = Directory.GetDirectories(MediaRoot);

        foreach (var dir in topLevelDirs)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var dirName = Path.GetFileName(dir);
            var subDirs = Directory.GetDirectories(dir);
            var directFiles = Directory.GetFiles(dir);

            // Check if this is the "Peliculas" folder (special case: videos)
            if (dirName.Equals("Peliculas", StringComparison.OrdinalIgnoreCase))
            {
                newVideos += ScanVideosFolder(dir, context, existingPaths, cancellationToken);
                continue;
            }

            // Determine pattern: artist (has subdirs) or standalone album (only files)
            bool hasSubDirs = subDirs.Length > 0;
            bool hasMediaFiles = directFiles.Any(f => IsMediaFile(f));

            if (hasSubDirs)
            {
                // Pattern: Artist folder with album subdirectories
                var artist = await GetOrCreateArtist(dirName, dir, context, existingArtists);
                if (!existingArtists.ContainsKey(artist.Nombre))
                {
                    existingArtists[artist.Nombre] = artist;
                    newArtists++;
                }

                foreach (var albumDir in subDirs)
                {
                    var albumName = Path.GetFileName(albumDir);
                    var coverPath = FindCoverImage(albumDir);

                    // Set artist photo from first album cover if not set
                    if (string.IsNullOrEmpty(artist.FotoUrl) && coverPath != null)
                    {
                        artist.FotoUrl = coverPath;
                    }

                    var albumFiles = Directory.GetFiles(albumDir);
                    foreach (var filePath in albumFiles)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        if (!IsAudioFile(filePath) || existingPaths.Contains(filePath)) continue;

                        var mediaItem = CreateMediaItem(filePath, albumName, coverPath, artist, MediaType.Audio);
                        context.MediaItems.Add(mediaItem);
                        existingPaths.Add(filePath);
                        newTracks++;
                    }
                }

                // Also scan direct files in artist folder (loose tracks)
                foreach (var filePath in directFiles)
                {
                    if (!IsAudioFile(filePath) || existingPaths.Contains(filePath)) continue;

                    var coverPath = FindCoverImage(dir);
                    var mediaItem = CreateMediaItem(filePath, dirName, coverPath, artist, MediaType.Audio);
                    context.MediaItems.Add(mediaItem);
                    existingPaths.Add(filePath);
                    newTracks++;
                }
            }
            else if (hasMediaFiles)
            {
                // Pattern: Standalone album folder (no subdirectories, files directly)
                var artistName = ExtractArtistFromAlbumFolder(dirName);
                var artist = await GetOrCreateArtist(artistName, dir, context, existingArtists);
                if (!existingArtists.ContainsKey(artist.Nombre))
                {
                    existingArtists[artist.Nombre] = artist;
                    newArtists++;
                }

                var coverPath = FindCoverImage(dir);
                if (string.IsNullOrEmpty(artist.FotoUrl) && coverPath != null)
                {
                    artist.FotoUrl = coverPath;
                }

                foreach (var filePath in directFiles)
                {
                    if (!IsAudioFile(filePath) || existingPaths.Contains(filePath)) continue;

                    var mediaItem = CreateMediaItem(filePath, dirName, coverPath, artist, MediaType.Audio);
                    context.MediaItems.Add(mediaItem);
                    existingPaths.Add(filePath);
                    newTracks++;
                }
            }

            // Batch save every artist folder to avoid huge memory usage
            if (context.ChangeTracker.Entries().Count() > 100)
            {
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("╔══════════════════════════════════════════╗");
        _logger.LogInformation("║   FV-CORE Scan Complete                 ║");
        _logger.LogInformation("║   New Artists: {Artists,-25}║", newArtists);
        _logger.LogInformation("║   New Tracks:  {Tracks,-25}║", newTracks);
        _logger.LogInformation("║   New Videos:  {Videos,-25}║", newVideos);
        _logger.LogInformation("╚══════════════════════════════════════════╝");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task<int> ScanVideosFolderAsync(string videosPath, FvCoreDbContext context, HashSet<string> existingPaths, CancellationToken ct)
    {
        return Task.FromResult(ScanVideosFolder(videosPath, context, existingPaths, ct));
    }

    private int ScanVideosFolder(string videosPath, FvCoreDbContext context, HashSet<string> existingPaths, CancellationToken ct)
    {
        int count = 0;
        var videoDirs = Directory.GetDirectories(videosPath);

        foreach (var movieDir in videoDirs)
        {
            var movieName = Path.GetFileName(movieDir);
            var files = Directory.GetFiles(movieDir, "*.*", SearchOption.AllDirectories);

            foreach (var filePath in files)
            {
                if (ct.IsCancellationRequested) break;
                if (!IsVideoFile(filePath) || existingPaths.Contains(filePath)) continue;

                var coverPath = FindCoverImage(Path.GetDirectoryName(filePath) ?? movieDir);

                var mediaItem = new MediaItem
                {
                    Titulo = Path.GetFileNameWithoutExtension(filePath),
                    RutaArchivo = filePath,
                    Tipo = MediaType.Video,
                    AlbumName = movieName,
                    CoverPath = coverPath,
                    Genero = "Video",
                    Calidad = GuessVideoQuality(filePath),
                    IsFavorite = false
                };

                context.MediaItems.Add(mediaItem);
                existingPaths.Add(filePath);
                count++;
            }
        }

        return count;
    }

    private MediaItem CreateMediaItem(string filePath, string? albumName, string? coverPath, Artist artist, MediaType type)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var genre = _genreClassifier.GetGenreFromAI(fileName, artist.Nombre, albumName);

        return new MediaItem
        {
            Titulo = CleanTrackTitle(fileName),
            RutaArchivo = filePath,
            Tipo = type,
            AlbumName = albumName,
            CoverPath = coverPath,
            Genero = genre,
            Calidad = type == MediaType.Audio ? GuessAudioQuality(filePath) : GuessVideoQuality(filePath),
            ArtistId = artist.Id,
            Artist = artist,
            IsFavorite = false
        };
    }

    private async Task<Artist> GetOrCreateArtist(string name, string dirPath, FvCoreDbContext context, Dictionary<string, Artist> cache)
    {
        if (cache.TryGetValue(name, out var existing))
            return existing;

        var artist = new Artist
        {
            Nombre = name,
            FotoUrl = FindCoverImage(dirPath)
        };

        context.Artists.Add(artist);
        await context.SaveChangesAsync();
        cache[name] = artist;
        return artist;
    }

    private static string? FindCoverImage(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return null;

        // Check known cover file names first
        foreach (var coverName in CoverFileNames)
        {
            var coverPath = Path.Combine(directoryPath, coverName);
            if (File.Exists(coverPath))
                return coverPath;
        }

        // Fallback: first image file in directory
        try
        {
            var firstImage = Directory.GetFiles(directoryPath)
                .FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f)));
            return firstImage;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts artist name from standalone album folder names.
    /// Patterns: "1986 - The Final Countdown" → "The Final Countdown"
    ///           "Depeche Mode Violator" → "Depeche Mode Violator"
    /// </summary>
    private static string ExtractArtistFromAlbumFolder(string folderName)
    {
        // Pattern: "Year - Album Name" → use Album Name as artist
        var yearPattern = System.Text.RegularExpressions.Regex.Match(folderName, @"^\d{4}\s*[-–]\s*(.+)$");
        if (yearPattern.Success)
            return yearPattern.Groups[1].Value.Trim();

        // Pattern: "Artist - Year - Album" → use Artist
        var artistYearPattern = System.Text.RegularExpressions.Regex.Match(folderName, @"^(.+?)\s*[-–]\s*\d{4}");
        if (artistYearPattern.Success)
            return artistYearPattern.Groups[1].Value.Trim();

        return folderName;
    }

    /// <summary>
    /// Cleans track titles by removing common prefixes like "01 - ", "Track 01", etc.
    /// </summary>
    private static string CleanTrackTitle(string fileName)
    {
        // Remove "01 - " or "01. " prefixes
        var cleaned = System.Text.RegularExpressions.Regex.Replace(fileName, @"^\d{1,3}\s*[-–.]\s*", "");
        // Remove "Copia de " prefix
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^Copia de\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return string.IsNullOrWhiteSpace(cleaned) ? fileName : cleaned.Trim();
    }

    private static string GuessAudioQuality(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var sizeKb = fileInfo.Length / 1024.0;
            // Very rough estimate: MP3 at 320kbps ≈ 2.4MB per minute
            // At 128kbps ≈ 1MB per minute
            return sizeKb > 5000 ? "320kbps" : sizeKb > 3000 ? "256kbps" : sizeKb > 1500 ? "192kbps" : "128kbps";
        }
        catch
        {
            return "128kbps";
        }
    }

    private static string GuessVideoQuality(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            return sizeMb > 4000 ? "1080p" : sizeMb > 1500 ? "720p" : sizeMb > 500 ? "480p" : "360p";
        }
        catch
        {
            return "SD";
        }
    }

    private static bool IsAudioFile(string filePath) => AudioExtensions.Contains(Path.GetExtension(filePath));
    private static bool IsVideoFile(string filePath) => VideoExtensions.Contains(Path.GetExtension(filePath));
    private static bool IsMediaFile(string filePath) => IsAudioFile(filePath) || IsVideoFile(filePath);
}
