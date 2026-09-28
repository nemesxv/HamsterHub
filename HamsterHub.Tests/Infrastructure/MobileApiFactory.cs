using HamsterHub.Data;
using HamsterHub.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HamsterHub.Tests.Infrastructure;

internal sealed class MobileApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly string mediaRoot = Path.Combine(AppContext.BaseDirectory, "test-media", Guid.NewGuid().ToString("N"));
    public const string Password = "Hamster123";
    public int ChildMemberId { get; private set; }
    public int ParentMemberId { get; private set; }
    public int ForeignMemberId { get; private set; }
    public int TaskId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();
        builder.UseEnvironment("Testing");
        Directory.CreateDirectory(mediaRoot);
        builder.UseWebRoot(mediaRoot);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
        });
    }

    public async Task SeedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        async Task<ApplicationUser> AddUser(string email, string role)
        {
            var user = new ApplicationUser { Email = email, UserName = email, DisplayName = role };
            var created = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(e => e.Description)));
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }
        var parent = await AddUser("parent@test.local", "Parent");
        var child = await AddUser("child@test.local", "Child");
        var foreign = await AddUser("foreign@test.local", "Parent");
        var household = new Household { Name = "Family", IsNameCustomized = true };
        var parentMember = new HouseholdMember { Household = household, User = parent, MemberRole = HouseholdMemberRole.Parent };
        var childMember = new HouseholdMember { Household = household, User = child, MemberRole = HouseholdMemberRole.Child };
        var foreignMember = new HouseholdMember { Household = new Household { Name = "Other" }, User = foreign,
            MemberRole = HouseholdMemberRole.Parent };
        var task = new CareTask { Household = household, Pet = new Pet { Household = household, Name = "Mochi", Species = "Hamster" },
            AssignedMember = childMember, CareCategoryId = 1, PointValue = 7, Frequency = CareTaskFrequency.Daily };
        db.HouseholdMembers.AddRange(parentMember, foreignMember);
        db.CareTasks.Add(task);
        await db.SaveChangesAsync();
        ChildMemberId = childMember.Id; ParentMemberId = parentMember.Id;
        ForeignMemberId = foreignMember.Id; TaskId = task.Id;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "test-media")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(mediaRoot).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(mediaRoot))
                Directory.Delete(mediaRoot, recursive: true);
        }
    }
}
