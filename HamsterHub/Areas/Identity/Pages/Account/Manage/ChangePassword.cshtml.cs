using System.ComponentModel.DataAnnotations;
using HamsterHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Areas.Identity.Pages.Account.Manage;

[Authorize(Roles = "Parent")]
public class ChangePasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IStringLocalizer<SharedResource> localizer) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Required")]
        [DataType(DataType.Password)]
        [Display(Name = "CurrentPassword")]
        public string OldPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "PasswordLength")]
        [DataType(DataType.Password)]
        [Display(Name = "NewPassword")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Required")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "PasswordsDoNotMatch")]
        [Display(Name = "ConfirmPassword")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        return user is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        var result = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty,
                    error.Code == "PasswordMismatch"
                        ? localizer["CurrentPasswordIncorrect"]
                        : localizer["PasswordUpdateFailed"]);
            }

            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = localizer["PasswordUpdated"];
        return RedirectToPage();
    }
}
