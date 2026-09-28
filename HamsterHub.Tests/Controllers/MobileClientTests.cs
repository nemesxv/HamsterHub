using System.Net;
using System.Net.Http.Json;
using HamsterHub.Client;
using HamsterHub.Contracts;

namespace HamsterHub.Tests.Controllers;

public sealed class MobileClientTests
{
    [Fact]
    public async Task ExpiredAccessToken_RefreshesAndPersistsSession_ThenRetriesOnce()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new("Bearer", "expired", 1, "refresh-token"));
        var calls = new List<string>();
        using var http = new HttpClient(new StubHandler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath.EndsWith("refresh"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new TokenResponse("Bearer", "fresh", 900, "new-refresh")) };
            return request.Headers.Authorization?.Parameter == "fresh"
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new SessionDto("Child", [])) }
                : new(HttpStatusCode.Unauthorized);
        })) { BaseAddress = new Uri("https://hamster.test/") };
        var client = new HamsterHubClient(http, store);
        Assert.True(await client.RestoreAsync());
        Assert.Equal("Child", (await client.GetSessionAsync()).DisplayName);
        Assert.Equal(["/api/v1/me", "/api/v1/auth/refresh", "/api/v1/me"], calls);
        Assert.Equal("new-refresh", store.Tokens!.RefreshToken);
    }

    [Fact]
    public async Task RejectedRefresh_ClearsStoredSession()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new("Bearer", "expired", 1, "revoked"));
        using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.Unauthorized)))
            { BaseAddress = new Uri("https://hamster.test/") };
        var client = new HamsterHubClient(http, store);
        await client.RestoreAsync();
        var error = await Assert.ThrowsAsync<MobileApiException>(client.GetSessionAsync);
        Assert.Equal("SessionExpired", error.Code);
        Assert.Null(store.Tokens);
    }

    [Fact]
    public async Task LogoutDuringRefresh_DoesNotRestoreTheSessionAfterLogout()
    {
        var store = new MemorySessionStore();
        await store.SaveAsync(new("Bearer", "expired", 1, "refresh"));
        var refreshing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncStubHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("refresh"))
            {
                refreshing.SetResult();
                await proceed.Task;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new TokenResponse("Bearer", "fresh", 900, "new-refresh")) };
            }
            return request.Headers.Authorization?.Parameter == "fresh"
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new SessionDto("Child", [])) }
                : new(HttpStatusCode.Unauthorized);
        })) { BaseAddress = new Uri("https://hamster.test/") };
        var client = new HamsterHubClient(http, store);
        await client.RestoreAsync();
        var requestTask = client.GetSessionAsync();
        await refreshing.Task.WaitAsync(TestContext.Current.CancellationToken);
        var logoutTask = client.LogoutAsync();
        proceed.SetResult();
        try { await requestTask; }
        catch (MobileApiException exception) { Assert.Equal("SessionExpired", exception.Code); }
        await logoutTask;
        Assert.Null(store.Tokens);
    }

    [Fact]
    public async Task FailedNetworkMutation_IsNotRetriedAutomatically()
    {
        var calls = 0;
        var store = new MemorySessionStore();
        await store.SaveAsync(new("Bearer", "valid", 900, "refresh"));
        using var http = new HttpClient(new StubHandler(_ =>
        { calls++; throw new HttpRequestException("Connection lost after sending"); }))
            { BaseAddress = new Uri("https://hamster.test/") };
        var client = new HamsterHubClient(http, store);
        await client.RestoreAsync();
        await Assert.ThrowsAsync<HttpRequestException>(() => client.CompleteAsync(1, 1, []));
        Assert.Equal(1, calls);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class AsyncStubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
