using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text.Json;
using HamsterHub.Contracts;

namespace HamsterHub.Controllers;

[AllowAnonymous]
public sealed class AppDownloadController(IWebHostEnvironment environment) : Controller
{
    [HttpGet("/download/android/version")]
    public async Task<IActionResult> Version(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var directory = Path.Combine(environment.ContentRootPath, "Downloads");
        try
        {
            await using var metadata = System.IO.File.OpenRead(Path.Combine(directory, "HamsterHub.version.json"));
            var release = await JsonSerializer.DeserializeAsync<AndroidReleaseDto>(metadata,
                new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
            if (!AndroidReleaseDto.IsValid(release)) return NotFound();
            await using var apk = System.IO.File.OpenRead(Path.Combine(directory, "HamsterHub.apk"));
            // A publication in progress must not advertise metadata for a different APK.
            if (apk.Length != release!.SizeBytes ||
                !Convert.ToHexString(await SHA256.HashDataAsync(apk, cancellationToken))
                    .Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Ok(release);
        }
        catch (IOException) { return NotFound(); }
        catch (JsonException) { return NotFound(); }
    }

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
