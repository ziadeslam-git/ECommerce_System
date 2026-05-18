using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities.Validation;

namespace ECommerce_System.ViewModels.Identity;

public class LoginVM
{
    [Required(ErrorMessage = "RequiredField")]
    [StrictEmailAddress(ErrorMessage = "Please enter a valid email address, for example example@gmail.com.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "RememberMe")]
    public bool RememberMe { get; set; } = true;
}
