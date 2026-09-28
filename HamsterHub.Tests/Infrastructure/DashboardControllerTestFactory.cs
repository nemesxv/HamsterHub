using System.Globalization;
using System.Security.Claims;
using HamsterHub.Controllers;
using HamsterHub.Data;
using HamsterHub.Models;
using HamsterHub.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HamsterHub.Tests.Infrastructure;

internal sealed class DashboardControllerTestFactory : IDisposable
{
    private readonly ApplicationDbContext dbContext;
    private readonly ServiceProvider serviceProvider;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly DateTimeOffset utcNow;

    public DashboardControllerTestFactory(
        ApplicationDbContext dbContext,
        DateTimeOffset utcNow)
    {
        this.dbContext = dbContext;
        this.utcNow = utcNow;
        serviceProvider = new ServiceCollection().BuildServiceProvider();
        userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(dbContext),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            serviceProvider,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    public DashboardController Create(string userId, string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        ],
        authenticationType: "Test"));
        var httpContext = new DefaultHttpContext
        {
            User = principal
        };
        var controller = new DashboardController(
            dbContext,
            userManager,
            new PassThroughLocalizer(),
            new PointBalanceService(dbContext),
            new RewardService(new FixedTimeProvider(utcNow)),
            new UploadedImageService(new TestWebHostEnvironment()),
            new CareWorkflowService(dbContext, new CareLogService(new FixedTimeProvider(utcNow)),
                new PointBalanceService(dbContext), new UploadedImageService(new TestWebHostEnvironment())))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(
                httpContext,
                new InMemoryTempDataProvider())
        };

        return controller;
    }

    public void Dispose()
    {
        userManager.Dispose();
        serviceProvider.Dispose();
    }

    private sealed class PassThroughLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(
                name,
                string.Format(CultureInfo.InvariantCulture, name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            [];
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> values = [];

        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(values);

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
            this.values = new Dictionary<string, object>(values);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HamsterHub.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
