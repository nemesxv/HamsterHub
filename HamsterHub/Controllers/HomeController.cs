using HamsterHub.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Diagnostics;

namespace HamsterHub.Controllers;

public class HomeController(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IStringLocalizer<SharedResource> localizer) : Controller
{
    public IActionResult Index(string? dialog)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new HomeViewModel
        {
            ActiveDialog = dialog is "login" or "signup" ? dialog : null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([Bind(Prefix = "Login")] LoginInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new HomeViewModel { Login = input, ActiveDialog = "login" });
        }

        var result = await signInManager.PasswordSignInAsync(
            input.Email, input.Password, input.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToAction(nameof(TwoFactor), new { rememberMe = input.RememberMe });
        }

        ModelState.AddModelError(string.Empty,
            result.IsLockedOut
                ? localizer["LockedOut"]
                : localizer["InvalidLogin"]);

        return View("Index", new HomeViewModel { Login = input, ActiveDialog = "login" });
    }

    [HttpGet]
    public async Task<IActionResult> TwoFactor(bool rememberMe = false)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Index), new { dialog = "login" });
        }

        return View(new TwoFactorLoginInputModel { RememberMe = rememberMe });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactor(TwoFactorLoginInputModel input)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Index), new { dialog = "login" });
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        var code = input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code,
            input.RememberMe,
            input.RememberMachine);

        if (result.Succeeded)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        ModelState.AddModelError(
            nameof(input.Code),
            result.IsLockedOut
                ? localizer["LockedOut"]
                : localizer["InvalidAuthenticatorCode"]);

        return View(input);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register([Bind(Prefix = "Register")] RegisterInputModel input)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new HomeViewModel { Register = input, ActiveDialog = "signup" });
        }

        var email = input.Email.Trim();
        var user = new ApplicationUser
        {
            DisplayName = input.DisplayName.Trim(),
            Email = email,
            UserName = email
        };

        var result = await userManager.CreateAsync(user, input.Password);

        if (result.Succeeded)
        {
            var roleResult = await userManager.AddToRoleAsync(user, "Parent");
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                ModelState.AddModelError(string.Empty, localizer["AccountCreationFailed"]);
                return View("Index", new HomeViewModel { Register = input, ActiveDialog = "signup" });
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Dashboard");
        }

        foreach (var error in result.Errors)
        {
            var message = error.Code is "DuplicateEmail" or "DuplicateUserName"
                ? localizer["EmailInUse"]
                : localizer["PasswordRequirements"];
            ModelState.AddModelError(string.Empty, message);
        }

        return View("Index", new HomeViewModel { Register = input, ActiveDialog = "signup" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetLanguage(string culture, string? returnUrl)
    {
        var selectedCulture = culture is "ru" or "en" ? culture : "ru";
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(selectedCulture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax
            });

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Action(nameof(Index))!);
    }

    public IActionResult Privacy() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
