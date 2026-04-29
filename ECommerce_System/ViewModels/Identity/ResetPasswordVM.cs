using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class ResetPasswordVM
{
    [Required(ErrorMessage = "RequiredField")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "InvalidEmailAddress")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MinLength(8, ErrorMessage = "MinimumLength")]
    [DataType(DataType.Password)]
    [Display(Name = "NewPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField")]
    [DataType(DataType.Password)]
    [Display(Name = "ConfirmPassword")]
    [Compare(nameof(NewPassword), ErrorMessage = "PasswordsDoNotMatch")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
