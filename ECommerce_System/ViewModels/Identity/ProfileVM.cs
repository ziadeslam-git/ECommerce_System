using System.ComponentModel.DataAnnotations;
using ECommerce_System.Utilities.Validation;
using Microsoft.AspNetCore.Http;

namespace ECommerce_System.ViewModels.Identity;

public class ProfileVM
{
    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "FullName")]
    public string FullName { get; set; } = string.Empty;

    [StrictEmailAddress(ErrorMessage = "Please enter a valid email address, for example example@gmail.com.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Display(Name = "PhoneNumber")]
    public string? PhoneNumber { get; set; }

    public string? PhoneCountryIso2 { get; set; } = "EG";

    public string? ProfileImageUrl { get; set; }

    public string? CroppedProfileImageDataUrl { get; set; }

    [Display(Name = "ProfilePhoto")]
    public IFormFile? ProfileImage { get; set; }
}
