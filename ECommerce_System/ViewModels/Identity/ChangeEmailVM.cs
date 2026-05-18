using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities.Validation;

namespace ECommerce_System.ViewModels.Identity;

public class ChangeEmailVM
{
    [Required(ErrorMessage = "RequiredField")]
    [StrictEmailAddress(ErrorMessage = "Please enter a valid email address, for example example@gmail.com.")]
    [Display(Name = "New Email Address")]
    public string NewEmail { get; set; } = string.Empty;
}
