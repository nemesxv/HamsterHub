using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HamsterHub.Contracts;

namespace HamsterHub.Mobile;

internal sealed record SavedAccount(string Id, string Server, string Email, string Name,
    TokenResponse? Tokens);

// Account identities and optional tokens live in Android encrypted storage; never store passwords.
internal static class SavedAccounts
{
    private const string StorageKey = "saved-family-accounts-v1";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool loaded;
    public static IReadOnlyList<SavedAccount> Items { get; private set; } = [];
    public static string IdFor(Uri server, string email) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(server.AbsoluteUri + "|" + email.Trim().ToUpperInvariant())));

    public static async Task LoadAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var json = await SecureStorage.Default.GetAsync(StorageKey);
            Items = json is null ? [] : JsonSerializer.Deserialize<List<SavedAccount>>(json) ?? [];
        }
        catch { Items = []; SecureStorage.Default.Remove(StorageKey); }
        finally { loaded = true; Gate.Release(); }
    }

    public static async Task SaveAsync(SavedAccount account)
    {
        await Gate.WaitAsync();
        try
        {
            var next = Items.Where(item => item.Id != account.Id).Prepend(account).Take(12).ToList();
            await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(next));
            Items = next;
        }
        finally { Gate.Release(); }
    }

    public static async Task UpdateTokensAsync(Uri server, TokenResponse tokens)
    {
        if (!loaded) await LoadAsync();
        var activeId = Preferences.Default.Get("active-saved-account", "");
        var account = Items.FirstOrDefault(item => item.Id == activeId && item.Server == server.AbsoluteUri && item.Tokens is not null);
        if (account is not null) await SaveAsync(account with { Tokens = tokens });
    }

    public static async Task RemoveAsync(string id)
    {
        await Gate.WaitAsync();
        try
        {
            var next = Items.Where(item => item.Id != id).ToList();
            await SecureStorage.Default.SetAsync(StorageKey, JsonSerializer.Serialize(next));
            Items = next;
        }
        finally { Gate.Release(); }
    }
}
