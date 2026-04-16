using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;

namespace FvCore.Controllers;

public class HomeController : Controller
{
    private readonly FvCoreDbContext _context;

    public HomeController(FvCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var totalTracks = await _context.MediaItems.CountAsync(m => m.Tipo == Models.MediaType.Audio);
        var totalVideos = await _context.MediaItems.CountAsync(m => m.Tipo == Models.MediaType.Video);
        var totalArtists = await _context.Artists.CountAsync();
        var totalPlaylists = await _context.Playlists.CountAsync();
        var totalFavorites = await _context.MediaItems.CountAsync(m => m.IsFavorite);

        var recentTracks = await _context.MediaItems
            .Include(m => m.Artist)
            .Where(m => m.Tipo == Models.MediaType.Audio)
            .OrderByDescending(m => m.Id)
            .Take(20)
            .ToListAsync();

        var topArtists = await _context.Artists
            .Include(a => a.MediaItems)
            .OrderByDescending(a => a.MediaItems.Count)
            .Take(12)
            .ToListAsync();

        var genres = await _context.MediaItems
            .Where(m => m.Genero != null)
            .GroupBy(m => m.Genero)
            .Select(g => new { Genre = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(10)
            .ToListAsync();

        ViewBag.TotalTracks = totalTracks;
        ViewBag.TotalVideos = totalVideos;
        ViewBag.TotalArtists = totalArtists;
        ViewBag.TotalPlaylists = totalPlaylists;
        ViewBag.TotalFavorites = totalFavorites;
        ViewBag.RecentTracks = recentTracks;
        ViewBag.TopArtists = topArtists;
        ViewBag.Genres = genres;

        return View();
    }
}
