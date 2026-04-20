using Microsoft.AspNetCore.Mvc;
using FvCore.Services;

namespace FvCore.Controllers;

[ApiController]
[Route("sync")]
public class SyncController : ControllerBase
{
    private readonly ScannerService _scannerService;

    public SyncController(ScannerService scannerService)
    {
        _scannerService = scannerService;
    }

    [HttpPost("force")]
    public async Task<IActionResult> ForceSync()
    {
        try
        {
            // SyncAsync(force: true) will update existing items and scan for new ones
            await _scannerService.SyncAsync(force: true);
            return Ok(new { success = true, message = "Sincronización completada correctamente." });
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
