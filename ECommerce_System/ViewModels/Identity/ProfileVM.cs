using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class ProfileVM
{
    [Required, MaxLength(100)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }
}
