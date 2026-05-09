using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using FvCore.Data;
using FvCore.Models;

namespace FvCore.Controllers;

public class MediaController : Controller
{
    private readonly FvCoreDbContext _context;
    private readonly IMemoryCache _cache;

    public MediaController(FvCoreDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<IActionResult> Music(string? search, string? genre, int page = 1)
    {
        try {
            const int pageSize = 200;

            var query = _context.MediaItems
                .AsNoTracking()
                .Include(m => m.Artist)
                .Where(m => m.Tipo == MediaType.Audio)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(m => m.Titulo.Contains(search) || (m.Artist != null && m.Artist.Nombre.Contains(search)));

            if (!string.IsNullOrWhiteSpace(genre))
                query = query.Where(m => m.Genero == genre);

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var tracks = await query
                .OrderBy(m => m.Artist!.Nombre).ThenBy(m => m.AlbumName).ThenBy(m => m.Titulo)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (!_cache.TryGetValue("Music_Genres", out List<string>? genres))
            {
                genres = await _context.MediaItems
                    .AsNoTracking()
                    .Where(m => m.Tipo == MediaType.Audio && m.Genero != null)
                    .Select(m => m.Genero!)
                    .Distinct()
                    .OrderBy(g => g)
                    .ToListAsync();

                _cache.Set("Music_Genres", genres, TimeSpan.FromHours(2));
            }

            ViewBag.Search = search;
            ViewBag.Genre = genre;
            ViewBag.Genres = genres;
            ViewBag.Page = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalItems = totalItems;

            if (tracks == null) tracks = new List<MediaItem>();

            return View(tracks);
        } catch (Exception ex) {
            return Content("Error controlado: " + ex.Message);
        }
    }

    public async Task<IActionResult> Videos(string? search, int page = 1)
    {
        // ── Fetch only the columns we need – avoids the EF Core GroupBy translation bug ──
        var rawQuery = _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Tipo == MediaType.Video && m.AlbumName != null)
            .Select(m => new { m.Id, m.AlbumName, m.Titulo });

        if (!string.IsNullOrWhiteSpace(search))
            rawQuery = rawQuery.Where(m => m.AlbumName!.Contains(search));

        var allVideos = await rawQuery.ToListAsync();

        // ── Group in memory – 100% safe, no SQL translation issues ──
        var folders = allVideos
            .GroupBy(m => m.AlbumName!)
            .Select(g => new VideoFolderViewModel
            {
                FolderName   = g.Key,
                VideoCount   = g.Count(),
                FirstVideoId = g.OrderBy(v => v.Titulo).First().Id
            })
            .OrderBy(f => f.FolderName)
            .ToList();

        const int pageSize  = 12;
        var totalItems      = folders.Count;
        var totalPages      = (int)Math.Ceiling(totalItems / (double)pageSize);

        var pagedFolders = folders
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        ViewBag.Search     = search;
        ViewBag.Page       = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;

        return View(pagedFolders);
    }

    [HttpGet("Media/VideoFolder")]
    public async Task<IActionResult> VideoFolder(string folderName, string? search, int page = 1)
    {
        if (string.IsNullOrEmpty(folderName)) return RedirectToAction("Videos");

        const int pageSize = 12;

        var query = _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Tipo == MediaType.Video && m.AlbumName == folderName)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => m.Titulo.Contains(search));

        var totalItems = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var videos = await query
            .OrderBy(m => m.Titulo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.FolderName = folderName;
        ViewBag.Search = search;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalItems = totalItems;
        
        // Find the first video ID for the folder download feature
        var firstVideo = await _context.MediaItems.FirstOrDefaultAsync(m => m.Tipo == MediaType.Video && m.AlbumName == folderName);
        ViewBag.FirstVideoId = firstVideo?.Id;

        return View(videos);
    }

    [HttpPost]
    public async Task<IActionResult> ToggleFavorite([FromBody] ToggleFavoriteRequest request)
    {
        var media = await _context.MediaItems.FindAsync(request.Id);
        if (media == null) return NotFound();

        media.IsFavorite = !media.IsFavorite;
        await _context.SaveChangesAsync();

        return Json(new { success = true, isFavorite = media.IsFavorite });
    }

    [HttpPost("Media/Sync")]
    public async Task<IActionResult> SyncDrive([FromServices] FvCore.Services.ScannerService scannerService, bool force = false)
    {
        try
        {
            await scannerService.SyncAsync(force);
            // Clear memory caches to avoid stale data
            if (_cache is MemoryCache mc)
            {
                mc.Compact(1.0);
            }
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Suggestions(int trackId, int count = 5)
    {
        var currentTrack = await _context.MediaItems
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == trackId);

        if (currentTrack == null) return Json(new List<object>());

        var query = _context.MediaItems
            .AsNoTracking()
            .Include(m => m.Artist)
            .Where(m => m.Tipo == MediaType.Audio && m.Id != trackId);

        var suggestions = new List<MediaItem>();
        var existingIds = new HashSet<int> { trackId }; // Prevent actual track and DB duplicates

        // 1. Same artist
        if (currentTrack.ArtistId.HasValue)
        {
            var artistSuggestions = await query
                .Where(m => m.ArtistId == currentTrack.ArtistId)
                .OrderBy(m => Guid.NewGuid())
                .Take(count)
                .ToListAsync();
            suggestions.AddRange(artistSuggestions);
            foreach (var s in artistSuggestions) existingIds.Add(s.Id);
        }

        // 2. Same genre
        if (suggestions.Count < count && !string.IsNullOrEmpty(currentTrack.Genero))
        {
            var genreSuggestions = await query
                .Where(m => m.Genero == currentTrack.Genero && !existingIds.Contains(m.Id))
                .OrderBy(m => Guid.NewGuid())
                .Take(count - suggestions.Count)
                .ToListAsync();
            
            suggestions.AddRange(genreSuggestions);
            foreach (var s in genreSuggestions) existingIds.Add(s.Id);
        }

        // 3. Fallback: completely random
        if (suggestions.Count < count)
        {
            var randomSuggestions = await query
                .Where(m => !existingIds.Contains(m.Id))
                .OrderBy(m => Guid.NewGuid())
                .Take(count - suggestions.Count)
                .ToListAsync();
                
            suggestions.AddRange(randomSuggestions);
        }

        System.Diagnostics.Debug.WriteLine($"[AUTOPLAY] Suggestions requested for trackId: {trackId}. Returning {suggestions.Count} tracks.");

        return Json(suggestions.Select(s => new
        {
            id = s.Id,
            title = s.Titulo,
            artist = s.Artist?.Nombre ?? "Desconocido",
            coverId = s.Id
        }));
    }

    [HttpGet]
    public async Task<IActionResult> Diagnostic()
    {
        try
        {
            var count = await _context.MediaItems.CountAsync();
            var audio = await _context.MediaItems.CountAsync(m => m.Tipo == MediaType.Audio);
            var samples = await _context.MediaItems.Take(5).Select(m => new { m.Titulo, m.ArtistName, m.AlbumName }).ToListAsync();
            return Json(new { Total = count, Audio = audio, Samples = samples });
        }
        catch (Exception ex) { return Json(new { Error = ex.Message }); }
    }
}


public class ToggleFavoriteRequest
{
    public int Id { get; set; }
}

public class VideoFolderViewModel
{
    public string FolderName { get; set; } = string.Empty;
    public int VideoCount { get; set; }
    public int FirstVideoId { get; set; }
}
