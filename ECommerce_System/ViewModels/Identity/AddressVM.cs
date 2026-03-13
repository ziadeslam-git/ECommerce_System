using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class AddressVM
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    [Display(Name = "Street Address")]
    public string Street { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    [Display(Name = "State / Province")]
    public string? State { get; set; }

    [Required, MaxLength(100)]
    [Display(Name = "Country")]
    public string Country { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    [Display(Name = "Postal Code")]
    public string PostalCode { get; set; } = string.Empty;

    [Display(Name = "Set as Default")]
    public bool IsDefault { get; set; }
}
