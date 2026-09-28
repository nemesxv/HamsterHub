using HamsterHub.Models;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace HamsterHub.Security;

public sealed class ActiveMobileUserRequirement : IAuthorizationRequirement;

public sealed class ActiveMobileUserHandler(SignInManager<ApplicationUser> signInManager)
    : AuthorizationHandler<ActiveMobileUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActiveMobileUserRequirement requirement)
    {
        var user = await signInManager.ValidateSecurityStampAsync(context.User);
        if (user is { IsActive: true } && await signInManager.CanSignInAsync(user) &&
            !await signInManager.UserManager.IsLockedOutAsync(user))
            context.Succeed(requirement);
    }
}

public static class MobileAuthentication
{
    public const string Policy = "MobileUser";

    public static IServiceCollection AddMobileAuthentication(this IServiceCollection services)
    {
        // Keep Identity's default cookie schemes for the website. API endpoints opt into bearer only.
        services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromDays(14);
        });
        services.AddScoped<IAuthorizationHandler, ActiveMobileUserHandler>();
        services.AddAuthorization(options => options.AddPolicy(Policy, policy =>
        {
            policy.AddAuthenticationSchemes(IdentityConstants.BearerScheme);
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new ActiveMobileUserRequirement());
        }));
        return services;
    }
}
