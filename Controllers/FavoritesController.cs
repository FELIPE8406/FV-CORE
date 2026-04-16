using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FvCore.Data;

namespace FvCore.Controllers;

public class FavoritesController : Controller
{
    private readonly FvCoreDbContext _context;

    public FavoritesController(FvCoreDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var favorites = await _context.MediaItems
            .Include(m => m.Artist)
            .Where(m => m.IsFavorite)
            .OrderBy(m => m.Artist!.Nombre)
            .ThenBy(m => m.Titulo)
            .ToListAsync();

        return View(favorites);
    }
}
