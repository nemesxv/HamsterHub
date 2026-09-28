using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HamsterHub.Contracts;

namespace HamsterHub.Client;

public interface ISessionStore
{
    Task<TokenResponse?> ReadAsync();
    Task SaveAsync(TokenResponse tokens);
    Task ClearAsync();
}

public sealed class MobileApiException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record UploadPhoto(string FileName, string ContentType, Func<Task<Stream>> OpenReadAsync);

// Shared, platform-independent client; the MAUI host supplies encrypted token storage.
public sealed class HamsterHubClient(HttpClient http, ISessionStore store)
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private TokenResponse? tokens;
    public string Culture { get; set; } = "ru";

    public async Task<bool> RestoreAsync()
    {
        tokens = await store.ReadAsync();
        return tokens is not null;
    }

    public async Task LoginAsync(string email, string password, string? code)
    {
        using var response = await http.PostAsJsonAsync("api/v1/auth/login", new LoginRequest(email, password, code));
        await CheckAsync(response);
        tokens = await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new MobileApiException("MobileConnectionError");
        await store.SaveAsync(tokens);
    }

    public async Task LogoutAsync()
    {
        await refreshLock.WaitAsync();
        try { await ClearSessionAsync(); }
        finally { refreshLock.Release(); }
    }

    private async Task ClearSessionAsync()
    {
        tokens = null;
        await store.ClearAsync();
    }

    public Task<SessionDto> GetSessionAsync() => GetAsync<SessionDto>("api/v1/me");
    public Task<DashboardDto> GetDashboardAsync(int memberId) =>
        GetAsync<DashboardDto>($"api/v1/memberships/{memberId}/dashboard");

    public async Task<byte[]> GetPhotoAsync(int memberId, string path)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Get,
            $"api/v1/memberships/{memberId}/media?path={Uri.EscapeDataString(path)}")));
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task CompleteAsync(int memberId, int taskId, IReadOnlyList<UploadPhoto> photos)
    {
        using var response = await SendAsync(async () =>
        {
            var content = new MultipartFormDataContent();
            try
            {
                // A field keeps the multipart body valid even when no photos were selected.
                content.Add(new StringContent("true"), "submitted");
                foreach (var photo in photos)
                {
                    var stream = new StreamContent(await photo.OpenReadAsync());
                    stream.Headers.ContentType = new MediaTypeHeaderValue(photo.ContentType);
                    content.Add(stream, "photos", photo.FileName);
                }
                return new HttpRequestMessage(HttpMethod.Post,
                    $"api/v1/memberships/{memberId}/tasks/{taskId}/complete") { Content = content };
            }
            catch { content.Dispose(); throw; }
        });
    }

    public async Task ReviewAsync(int memberId, int logId, bool approve)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Post,
            $"api/v1/memberships/{memberId}/care-logs/{logId}/review")
            { Content = JsonContent.Create(new ReviewRequest(approve)) }));
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, path)));
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new MobileApiException("MobileConnectionError");
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpRequestMessage>> create)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = tokens?.AccessToken ?? throw new MobileApiException("SessionExpired");
            using var request = await create();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.AcceptLanguage.ParseAdd(Culture);
            // Query culture preserves the server's Russian default and existing browser culture behavior.
            var path = request.RequestUri!.OriginalString;
            request.RequestUri = new Uri(path + (path.Contains('?') ? "&" : "?") + "culture=" + Culture, UriKind.Relative);
            var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                await RefreshAsync(accessToken);
                continue;
            }
            try { await CheckAsync(response); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new MobileApiException("SessionExpired");
    }

    private async Task RefreshAsync(string previousAccessToken)
    {
        await refreshLock.WaitAsync();
        try
        {
            if (tokens is null) throw new MobileApiException("SessionExpired");
            if (tokens.AccessToken != previousAccessToken) return;
            using var response = await http.PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken));
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                await ClearSessionAsync();
                throw new MobileApiException("SessionExpired");
            }
            await CheckAsync(response);
            tokens = await response.Content.ReadFromJsonAsync<TokenResponse>()
                ?? throw new MobileApiException("MobileConnectionError");
            await store.SaveAsync(tokens);
        }
        finally { refreshLock.Release(); }
    }

    private static async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(); }
        catch (System.Text.Json.JsonException) { }
        throw new MobileApiException(error?.Code ?? (response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "SessionExpired",
            HttpStatusCode.Forbidden => "MobileAccessDenied",
            HttpStatusCode.TooManyRequests => "MobileTryLater",
            HttpStatusCode.RequestEntityTooLarge => "TaskPhotoTooLarge",
            _ => "MobileConnectionError"
        }));
    }
}
