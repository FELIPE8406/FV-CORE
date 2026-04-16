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
        const int pageSize = 50;

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

        return View(tracks);
    }

    public async Task<IActionResult> Videos(string? search)
    {
        var query = _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Tipo == MediaType.Video)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(m => m.Titulo.Contains(search) || (m.AlbumName != null && m.AlbumName.Contains(search)));

        var videos = await query
            .OrderBy(m => m.AlbumName).ThenBy(m => m.Titulo)
            .ToListAsync();

        ViewBag.Search = search;
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
}

public class ToggleFavoriteRequest
{
    public int Id { get; set; }
}
