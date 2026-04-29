using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ECommerce_System.ViewModels.Identity;

public class ProfileVM
{
    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "FullName")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "InvalidEmailAddress")]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Phone(ErrorMessage = "InvalidPhoneNumber")]
    [Display(Name = "PhoneNumber")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "CountryCode")]
    public string PhoneCountryCode { get; set; } = "+20";

    public string? ProfileImageUrl { get; set; }

    public string? CroppedProfileImageDataUrl { get; set; }

    [Display(Name = "ProfilePhoto")]
    public IFormFile? ProfileImage { get; set; }
}
