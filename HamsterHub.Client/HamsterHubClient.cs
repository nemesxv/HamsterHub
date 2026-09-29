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
    public Task<HouseholdHubDto> GetHouseholdAsync(int memberId) =>
        GetAsync<HouseholdHubDto>($"api/v1/memberships/{memberId}/household");

    public async Task<int> AddMemberAsync(int memberId, CreateMemberRequest request) =>
        (await PostForAsync<CreatedItemDto, CreateMemberRequest>($"api/v1/memberships/{memberId}/members", request)).Id;
    public async Task<int> AddPetAsync(int memberId, CreatePetRequest request) =>
        (await PostForAsync<CreatedItemDto, CreatePetRequest>($"api/v1/memberships/{memberId}/pets", request)).Id;
    public async Task<int> AddTaskAsync(int memberId, CreateTaskRequest request) =>
        (await PostForAsync<CreatedItemDto, CreateTaskRequest>($"api/v1/memberships/{memberId}/tasks", request)).Id;
    public async Task<int> AddRewardAsync(int memberId, CreateRewardRequest request) =>
        (await PostForAsync<CreatedItemDto, CreateRewardRequest>($"api/v1/memberships/{memberId}/rewards", request)).Id;
    public Task UpdateMemberAsync(int memberId, int id, UpdateMemberRequest request) =>
        PutAsync($"api/v1/memberships/{memberId}/members/{id}", request);
    public Task UpdatePetAsync(int memberId, int id, UpdatePetRequest request) =>
        PutAsync($"api/v1/memberships/{memberId}/pets/{id}", request);
    public Task UpdateTaskAsync(int memberId, int id, UpdateTaskRequest request) =>
        PutAsync($"api/v1/memberships/{memberId}/tasks/{id}", request);
    public Task ArchiveMemberAsync(int memberId, int id) =>
        DeleteAsync($"api/v1/memberships/{memberId}/members/{id}");
    public Task ArchivePetAsync(int memberId, int id) =>
        DeleteAsync($"api/v1/memberships/{memberId}/pets/{id}");
    public Task ArchiveTaskAsync(int memberId, int id) =>
        DeleteAsync($"api/v1/memberships/{memberId}/tasks/{id}");
    public Task RequestRewardAsync(int memberId, int rewardId) =>
        PostAsync($"api/v1/memberships/{memberId}/rewards/{rewardId}/request", new { });
    public Task ReviewRewardAsync(int memberId, int id, bool approve) =>
        PostAsync($"api/v1/memberships/{memberId}/reward-requests/{id}/review",
            new RewardDecisionRequest(approve));
    public Task PurchaseRewardAsync(int memberId, int rewardId, int childMemberId) =>
        PostAsync($"api/v1/memberships/{memberId}/rewards/purchase",
            new DirectRewardRequest(rewardId, childMemberId));

    public async Task UpdateMediaAsync(int memberId, string kind, int id, UploadPhoto? photo, bool remove = false)
    {
        using var response = await SendAsync(async () =>
        {
            var content = new MultipartFormDataContent();
            try
            {
                content.Add(new StringContent(remove.ToString()), "remove");
                if (photo is not null)
                {
                    var stream = new StreamContent(await photo.OpenReadAsync());
                    stream.Headers.ContentType = new MediaTypeHeaderValue(photo.ContentType);
                    content.Add(stream, "photo", photo.FileName);
                }
                return new HttpRequestMessage(HttpMethod.Put,
                    $"api/v1/memberships/{memberId}/media/{kind}/{id}") { Content = content };
            }
            catch { content.Dispose(); throw; }
        });
    }

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

    private async Task PostAsync<T>(string path, T value)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Post, path)
            { Content = JsonContent.Create(value) }));
    }

    private async Task<TResponse> PostForAsync<TResponse, TValue>(string path, TValue value)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Post, path)
            { Content = JsonContent.Create(value) }));
        return await response.Content.ReadFromJsonAsync<TResponse>() ??
            throw new MobileApiException("MobileConnectionError");
    }

    private async Task DeleteAsync(string path)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Delete, path)));
    }

    private async Task PutAsync<T>(string path, T value)
    {
        using var response = await SendAsync(() => Task.FromResult(new HttpRequestMessage(HttpMethod.Put, path)
            { Content = JsonContent.Create(value) }));
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
