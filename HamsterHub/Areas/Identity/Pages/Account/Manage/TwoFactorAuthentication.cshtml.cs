using HamsterHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Areas.Identity.Pages.Account.Manage;

[Authorize(Roles = "Parent")]
public class TwoFactorAuthenticationModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    public bool HasAuthenticator { get; private set; }
    public bool Is2faEnabled { get; private set; }
    public bool IsMachineRemembered { get; private set; }
    public int RecoveryCodesLeft { get; private set; }
    public string[]? NewRecoveryCodes { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        await LoadStateAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateRecoveryCodesAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return BadRequest();
        }

        NewRecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
        await LoadStateAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostForgetBrowserAsync()
    {
        await signInManager.ForgetTwoFactorClientAsync();
        StatusMessage = localizer["BrowserForgotten"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAuthenticatorAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = localizer["AuthenticatorReset"];
        return RedirectToPage("./EnableAuthenticator");
    }

    public async Task<IActionResult> OnPostDisableTwoFactorAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        var result = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = localizer["TwoFactorTurnedOff"];
        return RedirectToPage();
    }

    private async Task LoadStateAsync(ApplicationUser user)
    {
        HasAuthenticator = await userManager.GetAuthenticatorKeyAsync(user) is not null;
        Is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        IsMachineRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user);
        RecoveryCodesLeft = Is2faEnabled ? await userManager.CountRecoveryCodesAsync(user) : 0;
    }
}
