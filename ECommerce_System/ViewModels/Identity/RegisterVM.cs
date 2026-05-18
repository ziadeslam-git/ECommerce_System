using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities.Validation;

namespace ECommerce_System.ViewModels.Identity;

public class RegisterVM
{
    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "FullName")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField")]
    [StrictEmailAddress(ErrorMessage = "Please enter a valid email address, for example example@gmail.com.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [Display(Name = "PhoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    public string? PhoneCountryIso2 { get; set; } = "EG";

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
