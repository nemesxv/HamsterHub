using System.Security.Claims;
using HamsterHub.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HamsterHub.Tests.Controllers;

public sealed class DashboardControllerIndexTests
{
    [Fact]
    public void Index_Parent_RedirectsToParentDashboard()
    {
        var controller = CreateController("Parent");

        var result = Assert.IsType<RedirectToActionResult>(controller.Index());

        Assert.Equal(nameof(DashboardController.Parent), result.ActionName);
        Assert.Null(result.ControllerName);
    }

    [Fact]
    public void Index_Child_RedirectsToChildDashboard()
    {
        var controller = CreateController("Child");

        var result = Assert.IsType<RedirectToActionResult>(controller.Index());

        Assert.Equal(nameof(DashboardController.Child), result.ActionName);
        Assert.Null(result.ControllerName);
    }

    [Fact]
    public void Index_UserWithoutDashboardRole_ReturnsForbid()
    {
        var controller = CreateController();

        Assert.IsType<ForbidResult>(controller.Index());
    }

    [Fact]
    public void Index_UserInBothRoles_PrioritizesParentDashboard()
    {
        var controller = CreateController("Child", "Parent");

        var result = Assert.IsType<RedirectToActionResult>(controller.Index());

        Assert.Equal(nameof(DashboardController.Parent), result.ActionName);
    }

    private static DashboardController CreateController(params string[] roles)
    {
        var claims = roles
            .Select(role => new Claim(ClaimTypes.Role, role))
            .Prepend(new Claim(ClaimTypes.NameIdentifier, "test-user"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var controller = new DashboardController(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal
                }
            }
        };

        return controller;
    }
}
