using System.ComponentModel.DataAnnotations;
using System.Text.Encodings.Web;
using HamsterHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Areas.Identity.Pages.Account.Manage;

[Authorize(Roles = "Parent")]
public class EnableAuthenticatorModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IStringLocalizer<SharedResource> localizer,
    UrlEncoder urlEncoder) : PageModel
{
    private const string AuthenticatorUriFormat =
        "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string SharedKey { get; private set; } = string.Empty;
    public string AuthenticatorUri { get; private set; } = string.Empty;
    public bool SetupComplete { get; private set; }
    public string[]? RecoveryCodes { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Required")]
        [StringLength(7, MinimumLength = 6, ErrorMessage = "AuthenticatorCodeLength")]
        [DataType(DataType.Text)]
        [Display(Name = "VerificationCode")]
        public string Code { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        await LoadSharedKeyAndQrCodeUriAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }

        var verificationCode = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            verificationCode);

        if (!isValid)
        {
            ModelState.AddModelError("Input.Code", localizer["InvalidAuthenticatorCode"]);
            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        await signInManager.RefreshSignInAsync(user);

        if (await userManager.CountRecoveryCodesAsync(user) == 0)
        {
            RecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray();
        }

        SetupComplete = true;
        return Page();
    }

    private async Task LoadSharedKeyAndQrCodeUriAsync(ApplicationUser user)
    {
        var unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        }

        SharedKey = FormatKey(unformattedKey!);
        var email = await userManager.GetEmailAsync(user) ?? user.UserName ?? "HamsterHub";
        AuthenticatorUri = GenerateQrCodeUri(email, unformattedKey!);
    }

    private static string FormatKey(string unformattedKey)
    {
        return string.Join(" ", Enumerable.Range(0, (unformattedKey.Length + 3) / 4)
            .Select(index => unformattedKey.Substring(index * 4, Math.Min(4, unformattedKey.Length - index * 4))))
            .ToLowerInvariant();
    }

    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return string.Format(
            AuthenticatorUriFormat,
            urlEncoder.Encode("HamsterHub"),
            urlEncoder.Encode(email),
            unformattedKey);
    }
}
