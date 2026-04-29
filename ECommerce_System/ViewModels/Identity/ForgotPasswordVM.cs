using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class ForgotPasswordVM
{
    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "InvalidEmailAddress")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;
}
