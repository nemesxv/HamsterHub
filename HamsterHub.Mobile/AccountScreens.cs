using HamsterHub.Client;

namespace HamsterHub.Mobile;

public sealed partial class MainPage
{
    private void AddSavedAccountCards()
    {
        if (SavedAccounts.Items.Count == 0) return;
        body.Add(RoleSectionHeading("👪", L("MobileSavedAccounts")));
        foreach (var account in SavedAccounts.Items)
        {
            var content = new VerticalStackLayout { Spacing = 8 };
            var avatar = Text(account.Role == "Child" ? "🧒" : "🔒", 52); avatar.HorizontalTextAlignment = TextAlignment.Center; content.Add(avatar);
            var name = Text(account.Name, 23); name.FontAttributes = FontAttributes.Bold;
            content.Add(name); content.Add(Text(account.Email, 14));
            if (account.Server != ConfiguredServerAddress())
                content.Add(Text(new Uri(account.Server).Authority, 13));
            content.Add(Button(account.Tokens is null ? "MobileSignInWithPassword" : "SavedAccountSignIn", async () =>
            {
                if (account.Tokens is null) ShowLogin(account);
                else await SignInSavedAccountAsync(account);
            }));
            var options = new Grid { ColumnSpacing = 8,
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
            if (account.Tokens is not null)
                options.Add(SecondaryButton("MobileUsePassword", () => { ShowLogin(account); return Task.CompletedTask; }));
            options.Add(SecondaryButton("MobileForgetAccount", async () =>
            {
                if (await DisplayAlertAsync(L("MobileForgetAccount"), account.Name, L("Delete"), L("Cancel")))
                { await SavedAccounts.RemoveAsync(account.Id); ShowLogin(); }
            }), account.Tokens is null ? 0 : 1);
            if (account.Tokens is null) options.ColumnDefinitions.RemoveAt(1);
            content.Add(options); body.Add(Card(content, Mint, Color.FromArgb("25443E")));
        }
    }

    private async Task SignInSavedAccountAsync(SavedAccount account)
    {
        if (account.Tokens is null) { ShowLogin(account); return; }
        TaskNotifications.Clear();
        SaveServerOverride(account.Server); Connect(account.Server);
        Preferences.Default.Set("active-saved-account", account.Id);
        Preferences.Default.Set("restore-active-session", true);
        await new SecureSessionStore(server!).SaveAsync(account.Tokens);
        await api!.RestoreAsync();
        try
        {
            try { await LoadSessionAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            { if (!await RestoreCachedChildAsync()) throw; }
            await CheckUpdateAsync();
        }
        catch (MobileApiException exception) when (exception.Code is "SessionExpired" or "MobileAccessDenied" or "InvalidLogin")
        {
            await api.LogoutAsync();
            await SavedAccounts.SaveAsync(account with { Tokens = null });
            ShowLogin(account); message.Text = L("MobileSavedSessionExpired");
        }
    }

    private async Task SaveSignedInAccountAsync(string email, bool remember, bool direct)
    {
        Preferences.Default.Set("restore-active-session", remember && direct);
        var id = SavedAccounts.IdFor(server!, email);
        if (!remember)
        {
            await SavedAccounts.RemoveAsync(id);
            Preferences.Default.Remove("active-saved-account"); return;
        }
        var tokens = direct ? await new SecureSessionStore(server!).ReadAsync() : null;
        await SavedAccounts.SaveAsync(new(id, server!.AbsoluteUri, email.Trim(), session!.DisplayName, tokens, session.Memberships.Any(item => item.Role == "Parent") ? "Parent" : "Child"));
        Preferences.Default.Set("active-saved-account", id);
        await CacheDashboardAsync();
    }
}
