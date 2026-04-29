using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class ChangePasswordVM
{
    [Required(ErrorMessage = "RequiredField")]
    [DataType(DataType.Password)]
    [Display(Name = "CurrentPassword")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MinLength(8, ErrorMessage = "MinimumLength")]
    [DataType(DataType.Password)]
    [Display(Name = "NewPassword")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField")]
    [DataType(DataType.Password)]
    [Display(Name = "ConfirmNewPassword")]
    [Compare(nameof(NewPassword), ErrorMessage = "PasswordsDoNotMatch")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}
