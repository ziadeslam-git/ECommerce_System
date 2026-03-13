using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class ForgotPasswordVM
{
    [Required, EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
