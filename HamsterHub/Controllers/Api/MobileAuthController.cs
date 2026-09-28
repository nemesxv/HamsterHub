using HamsterHub.Contracts;
using HamsterHub.Models;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HamsterHub.Controllers.Api;

[ApiController, Route("api/v1/auth"), AllowAnonymous]
[EnableRateLimiting("MobileAuth")]
public sealed class MobileAuthController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> users,
    IOptionsMonitor<BearerTokenOptions> bearerOptions,
    TimeProvider clock) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var user = await users.FindByNameAsync(request.Email.Trim());
        if (user is not { IsActive: true }) return Unauthorized(new ApiError("InvalidLogin"));
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new ApiError("InvalidLogin"));

        if (await users.GetTwoFactorEnabledAsync(user))
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return Unauthorized(new ApiError("TwoFactorRequired"));
            var code = request.TwoFactorCode.Replace(" ", "").Replace("-", "");
            if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
            {
                await users.AccessFailedAsync(user);
                return Unauthorized(new ApiError("InvalidAuthenticatorCode"));
            }
            if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded)
                return Unauthorized(new ApiError("InvalidLogin"));
        }

        return SignIn(await signInManager.CreateUserPrincipalAsync(user), IdentityConstants.BearerScheme);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var ticket = bearerOptions.Get(IdentityConstants.BearerScheme)
            .RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expiry || expiry <= clock.GetUtcNow())
            return Unauthorized(new ApiError("SessionExpired"));
        var user = await signInManager.ValidateSecurityStampAsync(ticket.Principal);
        if (user is not { IsActive: true } || !await signInManager.CanSignInAsync(user) ||
            await users.IsLockedOutAsync(user))
            return Unauthorized(new ApiError("SessionExpired"));
        return SignIn(await signInManager.CreateUserPrincipalAsync(user), IdentityConstants.BearerScheme);
    }
}
