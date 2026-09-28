namespace HamsterHub.Services;

public sealed class UploadedImageService(IWebHostEnvironment environment)
{
    private const long MaximumPetPhotoBytes = 5 * 1024 * 1024;
    private const int MaximumCompletionPhotos = 8;
    private static readonly HashSet<string> AllowedPhotoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly HashSet<string> AllowedPhotoContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public async Task<(bool Success, string? Path, string? ErrorKey)> SaveAsync(
        IFormFile photo,
        string folder,
        string invalidImageKey,
        string tooLargeKey)
    {
        var extension = Path.GetExtension(photo.FileName);
        if (photo.Length == 0 ||
            !AllowedPhotoExtensions.Contains(extension) ||
            !AllowedPhotoContentTypes.Contains(photo.ContentType) ||
            !string.Equals(photo.ContentType, extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => ""
            }, StringComparison.OrdinalIgnoreCase) ||
            !await HasValidImageSignatureAsync(photo, extension))
        {
            return (false, null, invalidImageKey);
        }

        if (photo.Length > MaximumPetPhotoBytes)
        {
            return (false, null, tooLargeKey);
        }

        var uploadDirectory = Path.Combine(environment.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(uploadDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(uploadDirectory, fileName);
        try
        {
            await using var stream = System.IO.File.Create(path);
            await photo.CopyToAsync(stream);
        }
        catch
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            throw;
        }
        return (true, $"/uploads/{folder}/{fileName}", null);
    }

    private static async Task<bool> HasValidImageSignatureAsync(IFormFile photo, string extension)
    {
        var header = new byte[12];
        await using var stream = photo.OpenReadStream();
        var bytesRead = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return bytesRead >= 3 &&
                   header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            return bytesRead >= pngSignature.Length &&
                   header.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature);
        }

        return bytesRead >= 12 &&
               header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
               header.AsSpan(8, 4).SequenceEqual("WEBP"u8);
    }

    public void Delete(string? photoPath, string folder)
    {
        if (string.IsNullOrWhiteSpace(photoPath))
        {
            return;
        }

        var relativePath = photoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(environment.WebRootPath, relativePath));
        var uploadRoot = Path.GetFullPath(
            Path.Combine(environment.WebRootPath, "uploads", folder)) +
            Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase) &&
            System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

}
