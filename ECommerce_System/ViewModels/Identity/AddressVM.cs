using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Identity;

public class AddressVM
{
    public int Id { get; set; }

    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "FullName")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MaxLength(20, ErrorMessage = "MaximumLength")]
    [Display(Name = "PhoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MaxLength(200, ErrorMessage = "MaximumLength")]
    [Display(Name = "StreetAddress")]
    public string Street { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "StateProvince")]
    public string? State { get; set; }

    [Required(ErrorMessage = "RequiredField"), MaxLength(100, ErrorMessage = "MaximumLength")]
    [Display(Name = "Country")]
    public string Country { get; set; } = string.Empty;

    [Required(ErrorMessage = "RequiredField"), MaxLength(20, ErrorMessage = "MaximumLength")]
    [Display(Name = "PostalCode")]
    public string PostalCode { get; set; } = string.Empty;

    [Display(Name = "SetAsDefault")]
    public bool IsDefault { get; set; }
}
