using System.ComponentModel.DataAnnotations;

namespace HamsterHub.Models;

public class HomeViewModel
{
    public LoginInputModel Login { get; set; } = new();
    public RegisterInputModel Register { get; set; } = new();
    public string? ActiveDialog { get; set; }
}

public class LoginInputModel
{
    [Required(ErrorMessage = "Required"), EmailAddress(ErrorMessage = "InvalidEmail")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required"), DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "KeepSignedIn")]
    public bool RememberMe { get; set; }
}

public class TwoFactorLoginInputModel
{
    [Required(ErrorMessage = "Required")]
    [StringLength(7, MinimumLength = 6, ErrorMessage = "AuthenticatorCodeLength")]
    [DataType(DataType.Text)]
    [Display(Name = "VerificationCode")]
    public string Code { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    [Display(Name = "RememberThisDevice")]
    public bool RememberMachine { get; set; }
}

public class RegisterInputModel
{
    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "DisplayNameLength")]
    [Display(Name = "DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required"), EmailAddress(ErrorMessage = "InvalidEmail")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "PasswordLength")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Required"), DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "PasswordsDoNotMatch")]
    [Display(Name = "ConfirmPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
