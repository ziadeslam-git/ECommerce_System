using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class LoginVM
{
    [Required(ErrorMessage = "RequiredField"), EmailAddress(ErrorMessage = "InvalidEmailAddress")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "RememberMe")]
    public bool RememberMe { get; set; } = true;
}
