using System.ComponentModel.DataAnnotations;
using HamsterHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Areas.Identity.Pages.Account.Manage;

[Authorize(Roles = "Parent")]
public class IndexModel(
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
        [StringLength(100, MinimumLength = 2, ErrorMessage = "DisplayNameLength")]
        [Display(Name = "DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Required")]
        [EmailAddress(ErrorMessage = "InvalidEmail")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound();
        }

        Input.DisplayName = user.DisplayName;
        Input.Email = user.Email ?? string.Empty;
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
            return Page();
        }

        var email = Input.Email.Trim();
        var emailOwner = await userManager.FindByEmailAsync(email);
        if (emailOwner is not null && emailOwner.Id != user.Id)
        {
            ModelState.AddModelError("Input.Email", localizer["EmailInUse"]);
            return Page();
        }

        user.DisplayName = Input.DisplayName.Trim();
        user.Email = email;
        user.UserName = email;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            StatusMessage = localizer["AccountUpdateFailed"].Value;
            return RedirectToPage();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = localizer["AccountUpdated"].Value;
        return RedirectToPage();
    }
}
