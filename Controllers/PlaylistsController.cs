using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using FvCore.Models;

namespace FvCore.Controllers;

public class PlaylistsController : Controller
{
    private readonly FvCoreDbContext _context;

    public PlaylistsController(FvCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var playlists = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.PlaylistMediaItems)
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync();

        return View(playlists);
    }

    public async Task<IActionResult> Details(int id)
    {
        var playlist = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.PlaylistMediaItems)
                .ThenInclude(pmi => pmi.MediaItem)
                    .ThenInclude(m => m.Artist)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (playlist == null) return NotFound();
        return View(playlist);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePlaylistRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre))
            return BadRequest("Playlist name is required");

        var playlist = new Playlist
        {
            Nombre = request.Nombre,
            FechaCreacion = DateTime.UtcNow
        };

        _context.Playlists.Add(playlist);
        await _context.SaveChangesAsync();

        return Json(new { success = true, id = playlist.Id, nombre = playlist.Nombre });
    }

    [HttpPost]
    public async Task<IActionResult> AddTrack([FromBody] AddTrackRequest request)
    {
        var playlist = await _context.Playlists
            .Include(p => p.PlaylistMediaItems)
            .FirstOrDefaultAsync(p => p.Id == request.PlaylistId);

        if (playlist == null) return NotFound();

        var exists = playlist.PlaylistMediaItems.Any(pmi => pmi.MediaItemId == request.MediaItemId);
        if (exists) return Json(new { success = false, message = "Track already in playlist" });

        var maxOrder = playlist.PlaylistMediaItems.Any()
            ? playlist.PlaylistMediaItems.Max(pmi => pmi.Orden)
            : 0;

        var pmi = new PlaylistMediaItem
        {
            PlaylistId = request.PlaylistId,
            MediaItemId = request.MediaItemId,
            Orden = maxOrder + 1
        };

        _context.PlaylistMediaItems.Add(pmi);
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }

    /// <summary>
    /// Batch add multiple tracks to a playlist. Skips duplicates and assigns order.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddTracksToPlaylist([FromBody] AddTracksRequest request)
    {
        if (request.MediaItemIds == null || request.MediaItemIds.Count == 0)
            return BadRequest(new { success = false, message = "No se seleccionaron pistas" });

        var playlist = await _context.Playlists
            .Include(p => p.PlaylistMediaItems)
            .FirstOrDefaultAsync(p => p.Id == request.PlaylistId);

        if (playlist == null) return NotFound(new { success = false, message = "Playlist no encontrada" });

        var existingIds = playlist.PlaylistMediaItems.Select(pmi => pmi.MediaItemId).ToHashSet();
        var maxOrder = playlist.PlaylistMediaItems.Any()
            ? playlist.PlaylistMediaItems.Max(pmi => pmi.Orden)
            : 0;

        int added = 0;
        int skipped = 0;

        foreach (var mediaId in request.MediaItemIds)
        {
            if (existingIds.Contains(mediaId))
            {
                skipped++;
                continue;
            }

            maxOrder++;
            _context.PlaylistMediaItems.Add(new PlaylistMediaItem
            {
                PlaylistId = request.PlaylistId,
                MediaItemId = mediaId,
                Orden = maxOrder
            });
            existingIds.Add(mediaId);
            added++;
        }

        await _context.SaveChangesAsync();

        var totalTracks = playlist.PlaylistMediaItems.Count;
        return Json(new { success = true, added, skipped, totalTracks });
    }

    /// <summary>
    /// Returns all playlists as JSON for the AJAX modal.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var playlists = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.PlaylistMediaItems)
            .OrderByDescending(p => p.FechaCreacion)
            .Select(p => new
            {
                p.Id,
                p.Nombre,
                TrackCount = p.PlaylistMediaItems.Count,
                Fecha = p.FechaCreacion.ToString("dd MMM yyyy")
            })
            .ToListAsync();

        return Json(playlists);
    }

    [HttpPost]
    public async Task<IActionResult> RemoveTrack([FromBody] AddTrackRequest request)
    {
        var pmi = await _context.PlaylistMediaItems
            .FirstOrDefaultAsync(p => p.PlaylistId == request.PlaylistId && p.MediaItemId == request.MediaItemId);

        if (pmi == null) return NotFound();

        _context.PlaylistMediaItems.Remove(pmi);
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> Delete([FromBody] DeletePlaylistRequest request)
    {
        var playlist = await _context.Playlists.FindAsync(request.Id);
        if (playlist == null) return NotFound();

        _context.Playlists.Remove(playlist);
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }
}

public class CreatePlaylistRequest { public string Nombre { get; set; } = ""; }
public class AddTrackRequest { public int PlaylistId { get; set; } public int MediaItemId { get; set; } }
public class AddTracksRequest { public int PlaylistId { get; set; } public List<int> MediaItemIds { get; set; } = new(); }
public class DeletePlaylistRequest { public int Id { get; set; } }
