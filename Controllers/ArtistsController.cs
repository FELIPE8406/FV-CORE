using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;

namespace FvCore.Controllers;

public class ArtistsController : Controller
{
    private readonly FvCoreDbContext _context;

    public ArtistsController(FvCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var query = _context.Artists
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a => a.Nombre.Contains(search));
        }

        var artists = await query
            .OrderBy(a => a.Nombre)
            .Select(a => new FvCore.Models.Artist
            {
                Id = a.Id,
                Nombre = a.Nombre,
                FotoUrl = a.FotoUrl,
                TrackCount = a.MediaItems.Count() // Executed directly in DB
            })
            .ToListAsync();

        ViewBag.Search = search;
        return View(artists);
    }

    public async Task<IActionResult> Details(int id)
    {
        var artist = await _context.Artists
            .AsNoTracking()
            .Include(a => a.MediaItems)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (artist == null) return NotFound();

        // Group tracks by album
        var albums = artist.MediaItems
            .GroupBy(m => m.AlbumName ?? "Singles")
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Titulo).ToList());

        ViewBag.Albums = albums;
        return View(artist);
    }
}
