using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HamsterHub.Client;
using HamsterHub.Contracts;
using HamsterHub.Data;
using HamsterHub.Models;
using HamsterHub.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HamsterHub.Tests.Controllers;

public sealed class MobileApiTests
{
    [Fact]
    public async Task RemindersAreParentConfiguredChildScopedAndSuppressedAfterSubmission()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var parentHttp = factory.CreateClient(); using var childHttp = factory.CreateClient();
        var parent = new HamsterHubClient(parentHttp, new MemorySessionStore());
        var child = new HamsterHubClient(childHttp, new MemorySessionStore());
        await parent.LoginAsync("parent@test.local", MobileApiFactory.Password, null);
        await child.LoginAsync("child@test.local", MobileApiFactory.Password, null);
        var settings = new TaskReminderDto(new(18, 0), new(2026, 10, 1), "Europe/Moscow");
        var taskId = await parent.AddTaskAsync(factory.ParentMemberId,
            new CreateTaskRequest(null, factory.ChildMemberId, null, null, "Once", 5, "Tidy toys", settings));
        Assert.Equal(settings, (await parent.GetHouseholdAsync(factory.ParentMemberId)).Tasks.Single(t => t.Id == taskId).Reminder);
        var reminder = Assert.Single(await child.GetRemindersAsync(factory.ChildMemberId));
        Assert.Equal(taskId, reminder.TaskId); Assert.Equal(settings, reminder.Schedule);
        Assert.Empty(await parent.GetRemindersAsync(factory.ParentMemberId));
        await Assert.ThrowsAsync<MobileApiException>(() => child.GetRemindersAsync(factory.ForeignMemberId));
        await Assert.ThrowsAsync<MobileApiException>(() => child.AddTaskAsync(factory.ChildMemberId,
            new CreateTaskRequest(null, factory.ChildMemberId, null, null, "Daily", 5, "Forbidden", settings)));
        await Assert.ThrowsAsync<MobileApiException>(() => parent.AddTaskAsync(factory.ParentMemberId,
            new CreateTaskRequest(null, factory.ChildMemberId, null, null, "AsNeeded", 5, "Unlimited", settings)));
        await child.CompleteAsync(factory.ChildMemberId, taskId, []);
        Assert.Equal(DateTimeOffset.MaxValue, Assert.Single(await child.GetRemindersAsync(factory.ChildMemberId)).SuppressUntil);
        await Assert.ThrowsAsync<MobileApiException>(() => child.CompleteAsync(factory.ChildMemberId, taskId, []));
        await parent.UpdateTaskAsync(factory.ParentMemberId, taskId,
            new UpdateTaskRequest(null, factory.ChildMemberId, null, null, "AsNeeded", 5, "Tidy toys"));
        Assert.Empty(await child.GetRemindersAsync(factory.ChildMemberId));
        Assert.Null((await parent.GetHouseholdAsync(factory.ParentMemberId)).Tasks.Single(t => t.Id == taskId).Reminder);
        await parent.UpdateTaskAsync(factory.ParentMemberId, taskId,
            new UpdateTaskRequest(null, factory.ChildMemberId, null, null, "Weekly", 5, "Tidy toys", settings));
        Assert.NotNull(Assert.Single(await child.GetRemindersAsync(factory.ChildMemberId)).SuppressUntil);
        await parent.ArchiveTaskAsync(factory.ParentMemberId, taskId);
        Assert.Empty(await child.GetRemindersAsync(factory.ChildMemberId));
    }

    [Fact]
    public async Task ParentCanManageHouseholdFromMobileApi()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient();
        var parent = new HamsterHubClient(http, new MemorySessionStore());
        await parent.LoginAsync("parent@test.local", MobileApiFactory.Password, null);

        var initial = await parent.GetHouseholdAsync(factory.ParentMemberId);
        Assert.Equal(2, initial.Members.Count);
        Assert.Single(initial.Pets);
        Assert.Single(initial.Tasks);

        await parent.AddMemberAsync(factory.ParentMemberId,
            new CreateMemberRequest("Second child", "second@test.local", MobileApiFactory.Password, "Child"));
        var newPetId = await parent.AddPetAsync(factory.ParentMemberId, new CreatePetRequest("Pip", "Hamster", null));
        byte[] petPhoto = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        await parent.UpdateMediaAsync(factory.ParentMemberId, "pets", newPetId,
            new UploadPhoto("pip.png", "image/png", () => Task.FromResult<Stream>(new MemoryStream(petPhoto))));
        var expanded = await parent.GetHouseholdAsync(factory.ParentMemberId);
        var newChild = Assert.Single(expanded.Members, item => item.Email == "second@test.local");
        var newPet = Assert.Single(expanded.Pets, item => item.Name == "Pip");
        Assert.NotNull(newPet.PhotoPath);
        Assert.Equal(petPhoto, await parent.GetPhotoAsync(factory.ParentMemberId, newPet.PhotoPath));
        await parent.AddTaskAsync(factory.ParentMemberId, new CreateTaskRequest(newPet.Id, newChild.Id,
            expanded.Categories[0].Id, null, "Weekly", 12));
        await parent.AddRewardAsync(factory.ParentMemberId,
            new CreateRewardRequest("Movie night", 10, [newChild.Id]));

        var managed = await parent.GetHouseholdAsync(factory.ParentMemberId);
        Assert.Contains(managed.Tasks, item => item.PetId == newPet.Id && item.AssignedMemberId == newChild.Id);
        Assert.Contains(managed.Rewards, item => item.Name == "Movie night" &&
            item.VisibleToMemberIds.SequenceEqual([newChild.Id]));

        var newTask = managed.Tasks.Single(item => item.PetId == newPet.Id);
        await parent.UpdateMemberAsync(factory.ParentMemberId, newChild.Id,
            new UpdateMemberRequest("Updated child", "Child"));
        await parent.UpdatePetAsync(factory.ParentMemberId, newPet.Id,
            new UpdatePetRequest("Pip updated", "Syrian hamster", new DateOnly(2025, 1, 2)));
        await parent.UpdateTaskAsync(factory.ParentMemberId, newTask.Id,
            new UpdateTaskRequest(newPet.Id, newChild.Id, expanded.Categories[1].Id, null, "AsNeeded", 15));
        var updated = await parent.GetHouseholdAsync(factory.ParentMemberId);
        Assert.Contains(updated.Members, item => item.Id == newChild.Id && item.DisplayName == "Updated child");
        Assert.Contains(updated.Pets, item => item.Id == newPet.Id && item.Name == "Pip updated" &&
            item.BirthDate == new DateOnly(2025, 1, 2));
        Assert.Contains(updated.Tasks, item => item.Id == newTask.Id && item.Points == 15 &&
            item.Frequency == "AsNeeded");
        await parent.ArchiveTaskAsync(factory.ParentMemberId, newTask.Id);
        await parent.ArchivePetAsync(factory.ParentMemberId, newPet.Id);
        await parent.ArchiveMemberAsync(factory.ParentMemberId, newChild.Id);
        var archived = await parent.GetHouseholdAsync(factory.ParentMemberId);
        Assert.DoesNotContain(archived.Members, item => item.Id == newChild.Id);
        Assert.DoesNotContain(archived.Pets, item => item.Id == newPet.Id);
        Assert.DoesNotContain(archived.Tasks, item => item.Id == newTask.Id);
    }

    [Fact]
    public async Task ChildRewardRequestAndParentReviewShareWebsitePointRules()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var parentHttp = factory.CreateClient();
        var parent = new HamsterHubClient(parentHttp, new MemorySessionStore());
        await parent.LoginAsync("parent@test.local", MobileApiFactory.Password, null);
        await parent.AddRewardAsync(factory.ParentMemberId,
            new CreateRewardRequest("Tunnel", 7, [factory.ChildMemberId]));

        using var childHttp = factory.CreateClient();
        var child = new HamsterHubClient(childHttp, new MemorySessionStore());
        await child.LoginAsync("child@test.local", MobileApiFactory.Password, null);
        await child.CompleteAsync(factory.ChildMemberId, factory.TaskId, []);
        var careRequest = Assert.Single((await parent.GetDashboardAsync(factory.ParentMemberId)).PendingApprovals);
        await parent.ReviewAsync(factory.ParentMemberId, careRequest.Id, true);

        var catalog = await child.GetHouseholdAsync(factory.ChildMemberId);
        var reward = Assert.Single(catalog.Rewards);
        Assert.True(reward.CanAfford);
        await child.RequestRewardAsync(factory.ChildMemberId, reward.Id);
        var pending = Assert.Single((await parent.GetHouseholdAsync(factory.ParentMemberId)).RewardRequests);
        await parent.ReviewRewardAsync(factory.ParentMemberId, pending.Id, true);

        Assert.Equal(0, (await child.GetDashboardAsync(factory.ChildMemberId)).Balance);
        var history = Assert.Single((await child.GetHouseholdAsync(factory.ChildMemberId)).RewardHistory);
        Assert.Equal("Approved", history.Status);
        Assert.Equal(0, history.BalanceAfterApproval);
    }

    [Fact]
    public async Task ChildCompletion_ParentApproval_UsesSameWebsiteDataAndHistoricalPoints()
    {
        using var factory = new MobileApiFactory();
        await factory.SeedAsync();
        using var childHttp = factory.CreateClient();
        var child = new HamsterHubClient(childHttp, new MemorySessionStore());
        await child.LoginAsync("child@test.local", MobileApiFactory.Password, null);
        Assert.Single((await child.GetSessionAsync()).Memberships);
        var before = await child.GetDashboardAsync(factory.ChildMemberId);
        Assert.True(Assert.Single(before.Tasks).CanComplete);
        await child.CompleteAsync(factory.ChildMemberId, factory.TaskId, []);
        var after = await child.GetDashboardAsync(factory.ChildMemberId);
        Assert.Equal("Pending", Assert.Single(after.History).Status);
        Assert.False(Assert.Single(after.Tasks).CanComplete);
        Assert.Equal(0, after.Balance);
        var duplicate = await Assert.ThrowsAsync<MobileApiException>(() => child.CompleteAsync(factory.ChildMemberId, factory.TaskId, []));
        Assert.Equal("TaskAlreadyRecorded", duplicate.Code);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.CareTasks.SingleAsync(cancellationToken: TestContext.Current.CancellationToken)).PointValue = 99;
            await db.SaveChangesAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        using var parentHttp = factory.CreateClient();
        var parent = new HamsterHubClient(parentHttp, new MemorySessionStore());
        await parent.LoginAsync("parent@test.local", MobileApiFactory.Password, null);
        var pending = Assert.Single((await parent.GetDashboardAsync(factory.ParentMemberId)).PendingApprovals);
        await parent.ReviewAsync(factory.ParentMemberId, pending.Id, true);
        var approved = await child.GetDashboardAsync(factory.ChildMemberId);
        Assert.Equal(7, approved.Balance);
        Assert.Equal("Approved", Assert.Single(approved.History).Status);
        await Assert.ThrowsAsync<MobileApiException>(() => parent.ReviewAsync(factory.ParentMemberId, pending.Id, true));
        using var verifyScope = factory.Services.CreateScope();
        var persisted = await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CareLogs.SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(7, persisted.PointsAwarded);
        Assert.Equal(7, persisted.PointsTotalAfterApproval);
    }

    [Fact]
    public async Task Api_RejectsAnonymousForeignMembershipAndChildReview()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/me", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var tokens = await LoginAsync(http, "child@test.local");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"/api/v1/memberships/{factory.ForeignMemberId}/dashboard", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync(
            $"/api/v1/memberships/{factory.ChildMemberId}/care-logs/1/review", new ReviewRequest(true), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsJsonAsync(
            $"/api/v1/memberships/{factory.ChildMemberId}/members",
            new CreateMemberRequest("No", "blocked@test.local", MobileApiFactory.Password, "Child"),
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        using var content = new MultipartFormDataContent { { new StringContent("true"), "submitted" } };
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync(
            $"/api/v1/memberships/{factory.ParentMemberId}/tasks/{factory.TaskId}/complete", content, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RefreshAndAccess_RejectDeactivatedUsersAndChangedSecurityStamp()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient();
        var tokens = await LoginAsync(http, "child@test.local");
        var refreshed = await http.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var child = (await users.FindByNameAsync("child@test.local"))!;
            await users.UpdateSecurityStampAsync(child);
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/v1/me", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        tokens = await LoginAsync(http, "child@test.local");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var child = (await users.FindByNameAsync("child@test.local"))!;
            child.IsActive = false; await users.UpdateAsync(child);
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/v1/me", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(tokens.RefreshToken), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("child@test.local", MobileApiFactory.Password), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task TwoFactorRequiredAndPasswordLockout_AreEnforced()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("parent@test.local"))!;
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
        }
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("parent@test.local", MobileApiFactory.Password), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("TwoFactorRequired", (await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: TestContext.Current.CancellationToken))!.Code);
        response = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("parent@test.local", MobileApiFactory.Password, "invalid"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        for (var attempt = 0; attempt < 5; attempt++)
            await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("child@test.local", "wrong"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("child@test.local", MobileApiFactory.Password), cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task WebsiteCookiesStillWork_ButCannotAuthorizeMobileApi()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await http.GetStringAsync("/Home/Index?dialog=login", cancellationToken: TestContext.Current.CancellationToken);
        var token = WebUtility.HtmlDecode(Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        var login = await http.PostAsync("/Home/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Login.Email"] = "parent@test.local", ["Login.Password"] = MobileApiFactory.Password,
            ["__RequestVerificationToken"] = token
        }), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await http.GetAsync("/Dashboard/Index", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/me", cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        var noCsrf = await http.PostAsync("/Dashboard/CompleteTaskAsParent", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["careTaskId"] = factory.TaskId.ToString() }), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task UploadedPhoto_IsAvailableToOwnerAndParent_ButNotAnotherHousehold()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient();
        var child = new HamsterHubClient(http, new MemorySessionStore());
        await child.LoginAsync("child@test.local", MobileApiFactory.Password, null);
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        await child.CompleteAsync(factory.ChildMemberId, factory.TaskId,
            [new UploadPhoto("care.png", "image/png", () => Task.FromResult<Stream>(new MemoryStream(bytes)))]);
        var path = Assert.Single(Assert.Single((await child.GetDashboardAsync(factory.ChildMemberId)).History).Photos);
        Assert.Equal(bytes, await child.GetPhotoAsync(factory.ChildMemberId, path));
        using var parentHttp = factory.CreateClient();
        var parent = new HamsterHubClient(parentHttp, new MemorySessionStore());
        await parent.LoginAsync("parent@test.local", MobileApiFactory.Password, null);
        Assert.Equal(bytes, await parent.GetPhotoAsync(factory.ParentMemberId, path));
        using var foreignHttp = factory.CreateClient();
        var foreign = new HamsterHubClient(foreignHttp, new MemorySessionStore());
        await foreign.LoginAsync("foreign@test.local", MobileApiFactory.Password, null);
        await Assert.ThrowsAsync<MobileApiException>(() => foreign.GetPhotoAsync(factory.ForeignMemberId, path));
        await Assert.ThrowsAsync<MobileApiException>(() => child.GetPhotoAsync(factory.ChildMemberId, "/uploads/../appsettings.json"));
    }

    [Fact]
    public async Task Upload_RejectsMismatchedMimeAndOverLimitPhotoCount()
    {
        using var factory = new MobileApiFactory(); await factory.SeedAsync();
        using var http = factory.CreateClient();
        var client = new HamsterHubClient(http, new MemorySessionStore());
        await client.LoginAsync("child@test.local", MobileApiFactory.Password, null);
        var photo = new UploadPhoto("care.png", "image/jpeg", () => Task.FromResult<Stream>(
            new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0])));
        var invalid = await Assert.ThrowsAsync<MobileApiException>(() => client.CompleteAsync(factory.ChildMemberId, factory.TaskId, [photo]));
        Assert.Equal("InvalidTaskPhoto", invalid.Code);
        var count = await Assert.ThrowsAsync<MobileApiException>(() => client.CompleteAsync(factory.ChildMemberId,
            factory.TaskId, Enumerable.Repeat(photo, 9).ToList()));
        Assert.Equal("TooManyTaskPhotos", count.Code);
        Assert.Empty((await client.GetDashboardAsync(factory.ChildMemberId)).History);
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient http, string email)
    {
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, MobileApiFactory.Password), cancellationToken: TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: TestContext.Current.CancellationToken))!;
    }
}

internal sealed class MemorySessionStore : ISessionStore
{
    public TokenResponse? Tokens { get; private set; }
    public Task<TokenResponse?> ReadAsync() => Task.FromResult(Tokens);
    public Task SaveAsync(TokenResponse tokens) { Tokens = tokens; return Task.CompletedTask; }
    public Task ClearAsync() { Tokens = null; return Task.CompletedTask; }
}
