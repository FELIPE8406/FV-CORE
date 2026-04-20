using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;
using FvCore.Models;

namespace FvCore.Controllers;

[Route("api/playlists")]
public class PlaylistsController : Controller
{
    private readonly FvCoreDbContext _context;

    public PlaylistsController(FvCoreDbContext context)
    {
        _context = context;
    }

    [HttpGet("/Playlists")]
    public async Task<IActionResult> Index()
    {
        var playlists = await _context.Playlists
            .AsNoTracking()
            .Include(p => p.PlaylistMediaItems)
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync();

        return View(playlists);
    }

    [HttpGet("/Playlists/Details/{id}")]
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

    /// <summary>
    /// RESTful Rename (PUT /api/playlists/{id})
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] RenamePlaylistRequest request)
    {
        var nuevoNombre = request.NuevoNombre?.Trim();
        if (string.IsNullOrWhiteSpace(nuevoNombre))
            return BadRequest(new { success = false, message = "El nombre no es válido" });

        var playlist = await _context.Playlists.FindAsync(id);
        if (playlist == null) return NotFound();

        playlist.Nombre = nuevoNombre;
        await _context.SaveChangesAsync();

        return Json(new { success = true, nombre = playlist.Nombre });
    }

    /// <summary>
    /// RESTful Delete (DELETE /api/playlists/{id})
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(int id)
    {
        var playlist = await _context.Playlists.FindAsync(id);
        if (playlist == null) return NotFound();

        // Safe and efficient removal of relationships
        var items = _context.PlaylistMediaItems.Where(pmi => pmi.PlaylistId == id);
        _context.PlaylistMediaItems.RemoveRange(items);

        _context.Playlists.Remove(playlist);
        await _context.SaveChangesAsync();

        return Json(new { success = true });
    }

    // Keep legacy endpoints for internal back-compat if needed, or remove them
    // The user asked for specific RESTful ones, I'll keep the Rename/Delete logic consolidated above
    // but for now I'll just keep the ones they asked for.
}

public class CreatePlaylistRequest { public string Nombre { get; set; } = ""; }
public class AddTrackRequest { public int PlaylistId { get; set; } public int MediaItemId { get; set; } }
public class AddTracksRequest { public int PlaylistId { get; set; } public List<int> MediaItemIds { get; set; } = new(); }
public class DeletePlaylistRequest { public int Id { get; set; } }
public class RenamePlaylistRequest { public int Id { get; set; } public string NuevoNombre { get; set; } = ""; }
