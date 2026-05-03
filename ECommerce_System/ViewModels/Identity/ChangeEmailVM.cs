using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class ChangeEmailVM
{
    [Required, EmailAddress]
    [Display(Name = "New Email Address")]
    public string NewEmail { get; set; } = string.Empty;
}
