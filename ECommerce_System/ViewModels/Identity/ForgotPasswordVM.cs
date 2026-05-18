using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities.Validation;

namespace ECommerce_System.ViewModels.Identity;

public class ForgotPasswordVM
{
    [Required(ErrorMessage = "RequiredField")]
    [StrictEmailAddress(ErrorMessage = "Please enter a valid email address, for example example@gmail.com.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
