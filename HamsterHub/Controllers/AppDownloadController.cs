using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HamsterHub.Controllers;

[AllowAnonymous]
public sealed class AppDownloadController(IWebHostEnvironment environment) : Controller
{
    [HttpGet("/download/android")]
    [HttpHead("/download/android")]
    public IActionResult Android()
    {
        var path = Path.Combine(environment.ContentRootPath, "Downloads", "HamsterHub.apk");
        if (!System.IO.File.Exists(path))
            return NotFound();

        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/vnd.android.package-archive",
            "HamsterHub.apk", enableRangeProcessing: true);
    }
}
