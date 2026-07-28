using System.ComponentModel.DataAnnotations;
using HamsterHub.Data;
using HamsterHub.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace HamsterHub.Areas.Identity.Pages.Account.Manage;

[Authorize(Roles = "Parent")]
public class IndexModel(
    ApplicationDbContext dbContext,
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
        [StringLength(100, MinimumLength = 2, ErrorMessage = "HouseholdNameLength")]
        [Display(Name = "FamilyName")]
        public string FamilyName { get; set; } = string.Empty;

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

        var membership = await dbContext.HouseholdMembers
            .AsNoTracking()
            .Include(member => member.Household)
            .FirstOrDefaultAsync(member =>
                member.UserId == user.Id &&
                member.MemberRole == HouseholdMemberRole.Parent &&
                member.IsActive);
        Input.FamilyName = membership?.Household.IsNameCustomized == true
            ? membership.Household.Name
            : localizer["DefaultHouseholdName"].Value;
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

        var membership = await dbContext.HouseholdMembers
            .Include(member => member.Household)
            .FirstOrDefaultAsync(member =>
                member.UserId == user.Id &&
                member.MemberRole == HouseholdMemberRole.Parent &&
                member.IsActive);
        if (membership is null)
        {
            membership = new HouseholdMember
            {
                Household = new Household
                {
                    Name = localizer["DefaultHouseholdName"].Value
                },
                UserId = user.Id,
                MemberRole = HouseholdMemberRole.Parent
            };
            dbContext.HouseholdMembers.Add(membership);
        }

        var familyName = Input.FamilyName.Trim();
        var localizedDefaultName = localizer["DefaultHouseholdName"].Value;
        if (!membership.Household.IsNameCustomized &&
            string.Equals(
                familyName,
                localizedDefaultName,
                StringComparison.CurrentCulture))
        {
            membership.Household.Name = localizedDefaultName;
        }
        else
        {
            membership.Household.Name = familyName;
            membership.Household.IsNameCustomized = true;
        }

        user.DisplayName = Input.DisplayName.Trim();
        user.Email = email;
        user.UserName = email;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            StatusMessage = localizer["AccountUpdateFailed"].Value;
            return RedirectToPage();
        }

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = localizer["AccountUpdated"].Value;
        return RedirectToPage();
    }
}
