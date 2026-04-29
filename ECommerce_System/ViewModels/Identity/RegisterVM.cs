using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class RegisterVM
{
    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "FullName")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "InvalidEmailAddress")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MinLength(8, ErrorMessage = "MinimumLength")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField")]
    [DataType(DataType.Password)]
    [Display(Name = "ConfirmPassword")]
    [Compare(nameof(Password), ErrorMessage = "PasswordsDoNotMatch")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
