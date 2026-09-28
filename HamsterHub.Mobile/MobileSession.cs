using System.Globalization;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HamsterHub.Client;
using HamsterHub.Contracts;

namespace HamsterHub.Mobile;

internal static class Strings
{
    private static readonly ResourceManager Resources = new("HamsterHub.Mobile.Resources.SharedResource", typeof(App).Assembly);
    public static string Culture { get; set; } = Preferences.Default.Get("language", "ru");
    public static string Get(string key) => Resources.GetString(key, CultureInfo.GetCultureInfo(Culture)) ?? key;
}

internal sealed class SecureSessionStore(Uri server) : ISessionStore
{
    private readonly string key = "session-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.AbsoluteUri)));
    public async Task<TokenResponse?> ReadAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(key);
            return json is null ? null : JsonSerializer.Deserialize<TokenResponse>(json);
        }
        catch
        {
            SecureStorage.Default.Remove(key);
            return null;
        }
    }
    public Task SaveAsync(TokenResponse tokens) => SecureStorage.Default.SetAsync(key, JsonSerializer.Serialize(tokens));
    public Task ClearAsync() { SecureStorage.Default.Remove(key); return Task.CompletedTask; }
}

internal static class ServerAddress
{
    public static Uri Parse(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new MobileApiException("MobileServerInvalid");
        if (uri.Scheme == "https") return uri;
#if DEBUG
        if (uri.Scheme == "http" && uri.Host is "10.0.2.2" or "localhost" or "127.0.0.1") return uri;
#endif
        throw new MobileApiException("MobileServerInvalid");
    }
}
