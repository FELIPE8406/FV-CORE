using Microsoft.AspNetCore.Mvc;
using FvCore.Data;

namespace FvCore.Controllers;

/// <summary>
/// Streams media files with HTTP Range Processing support for seeking.
/// </summary>
public class StreamingController : Controller
{
    private readonly FvCoreDbContext _context;

    public StreamingController(FvCoreDbContext context)
    {
        _context = context;
    }

    [HttpGet("stream/{id:int}")]
    public IActionResult Stream(int id)
    {
        var media = _context.MediaItems.Find(id);
        if (media == null)
            return NotFound();

        if (!System.IO.File.Exists(media.RutaArchivo))
            return NotFound($"File not found: {media.RutaArchivo}");

        var extension = Path.GetExtension(media.RutaArchivo).ToLowerInvariant();
        var contentType = extension switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wma" => "audio/x-ms-wma",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".aac" => "audio/aac",
            ".mp4" => "video/mp4",
            ".avi" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".flv" => "video/x-flv",
            ".wmv" => "video/x-ms-wmv",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };

        return PhysicalFile(media.RutaArchivo, contentType, enableRangeProcessing: true);
    }
}
