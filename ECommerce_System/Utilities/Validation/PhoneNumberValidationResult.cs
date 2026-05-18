namespace ECommerce_System.Utilities.Validation;

public sealed record PhoneNumberValidationResult(
    bool IsValid,
    string? E164Number,
    string? RegionCode,
    string? ErrorMessage);
