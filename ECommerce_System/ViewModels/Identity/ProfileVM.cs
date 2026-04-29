using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ECommerce_System.ViewModels.Identity;

public class ProfileVM
{
    [Required, MaxLength(100)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Phone]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Country Code")]
    public string PhoneCountryCode { get; set; } = "+20";

    public string? ProfileImageUrl { get; set; }

    public string? CroppedProfileImageDataUrl { get; set; }

    [Display(Name = "Profile Photo")]
    public IFormFile? ProfileImage { get; set; }
}
