using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using HamsterHub.Contracts;

namespace HamsterHub.Client;

// Deliberately separate from the authenticated API: update requests carry no tokens.
public sealed class AndroidUpdateClient(HttpClient http)
{
    public async Task<AndroidReleaseDto?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("download/android/version", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<AndroidReleaseDto>(cancellationToken);
        if (!AndroidReleaseDto.IsValid(release)) throw new InvalidDataException("Invalid Android release.");
        return release;
    }

    public async Task DownloadAsync(AndroidReleaseDto release, string destination,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!AndroidReleaseDto.IsValid(release)) throw new InvalidDataException("Invalid Android release.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        if (File.Exists(destination))
        {
            await using var existing = File.OpenRead(destination);
            if (existing.Length == release.SizeBytes &&
                Convert.ToHexString(await SHA256.HashDataAsync(existing, cancellationToken))
                    .Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) return;
        }
        var temporary = destination + ".part";
        try
        {
            using var response = await http.GetAsync("download/android", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } size && size != release.SizeBytes)
                throw new InvalidDataException("Release changed during download.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += count;
                    if (received > release.SizeBytes) throw new InvalidDataException("Oversized APK.");
                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((double)received / release.SizeBytes);
                }
                if (received != release.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset())
                    .Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("APK checksum mismatch.");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
