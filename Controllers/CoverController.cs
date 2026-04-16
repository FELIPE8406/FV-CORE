using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;

namespace FvCore.Controllers;

/// <summary>
/// Serves cover images from local filesystem or returns default placeholder.
/// </summary>
public class CoverController : Controller
{
    private readonly FvCoreDbContext _context;
    private readonly IWebHostEnvironment _env;

    public CoverController(FvCoreDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    [HttpGet("cover/{id:int}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCover(int id)
    {
        var coverPath = await _context.MediaItems
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => m.CoverPath)
            .FirstOrDefaultAsync();

        if (coverPath != null && System.IO.File.Exists(coverPath))
        {
            var ext = Path.GetExtension(coverPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return PhysicalFile(coverPath, contentType);
        }

        // Return default placeholder
        var defaultPath = Path.Combine(_env.WebRootPath, "images", "default-cover.svg");
        return PhysicalFile(defaultPath, "image/svg+xml");
    }

    [HttpGet("cover/artist/{id:int}")]
    [ResponseCache(Duration = 31536000, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetArtistCover(int id)
    {
        var fotoUrl = await _context.Artists
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => a.FotoUrl)
            .FirstOrDefaultAsync();

        if (fotoUrl != null && System.IO.File.Exists(fotoUrl))
        {
            var ext = Path.GetExtension(fotoUrl).ToLowerInvariant();
            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
            return PhysicalFile(fotoUrl, contentType);
        }

        var defaultPath = Path.Combine(_env.WebRootPath, "images", "default-cover.svg");
        return PhysicalFile(defaultPath, "image/svg+xml");
    }
}
