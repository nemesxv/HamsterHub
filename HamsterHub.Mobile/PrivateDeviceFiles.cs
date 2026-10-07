using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HamsterHub.Mobile;

// Large drafts/photos stay in private files, authenticated-encrypted with a SecureStorage key.
internal static class PrivateDeviceFiles
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string PathFor(string name) => Path.Combine(FileSystem.AppDataDirectory,
        "family-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))) + ".bin");
    private static async Task<byte[]> KeyAsync()
    {
        const string name = "family-files-key-v1";
        var value = await SecureStorage.Default.GetAsync(name);
        if (value is not null) return Convert.FromBase64String(value);
        var key = RandomNumberGenerator.GetBytes(32);
        await SecureStorage.Default.SetAsync(name, Convert.ToBase64String(key));
        return key;
    }
    public static async Task SaveBytesAsync(string name, byte[] bytes)
    {
        await Gate.WaitAsync();
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var encrypted = new byte[bytes.Length];
            using var aes = new AesGcm(await KeyAsync(), 16);
            aes.Encrypt(nonce, bytes, encrypted, tag, Encoding.UTF8.GetBytes(name));
            var packed = nonce.Concat(tag).Concat(encrypted).ToArray();
            var path = PathFor(name);
            await File.WriteAllBytesAsync(path + ".tmp", packed);
            File.Move(path + ".tmp", path, true);
        }
        finally { Gate.Release(); }
    }
    public static async Task<byte[]?> ReadBytesAsync(string name)
    {
        await Gate.WaitAsync();
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return null;
            var packed = await File.ReadAllBytesAsync(path);
            if (packed.Length < 28) return null;
            var bytes = new byte[packed.Length - 28];
            using var aes = new AesGcm(await KeyAsync(), 16);
            aes.Decrypt(packed.AsSpan(0, 12), packed.AsSpan(28), packed.AsSpan(12, 16), bytes, Encoding.UTF8.GetBytes(name));
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or FormatException) { return null; }
        finally { Gate.Release(); }
    }
    public static Task SaveAsync<T>(string name, T value) => SaveBytesAsync(name, JsonSerializer.SerializeToUtf8Bytes(value));
    public static async Task<T?> ReadAsync<T>(string name)
    {
        var bytes = await ReadBytesAsync(name);
        try { return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes); }
        catch (JsonException) { return default; }
    }
    public static void Remove(string name) { var path = PathFor(name); if (File.Exists(path)) File.Delete(path); }
}
