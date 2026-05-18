using PhoneNumbers;

namespace ECommerce_System.Utilities.Validation;

public sealed class LibPhoneNumberValidator : IPhoneNumberValidator
{
    private readonly PhoneNumberUtil _phoneNumberUtil = PhoneNumberUtil.GetInstance();

    public PhoneNumberValidationResult ValidateAndFormat(string? phoneNumber, string? regionCode, bool isRequired)
    {
        var rawValue = phoneNumber?.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return isRequired
                ? Invalid("Phone number is required.")
                : new PhoneNumberValidationResult(true, null, null, null);
        }

        if (rawValue.Length > 32)
        {
            return Invalid("Phone number is too long.");
        }

        if (rawValue.Any(ch => !char.IsDigit(ch) && ch is not '+' and not ' ' and not '-' and not '(' and not ')'))
        {
            return Invalid("Phone number can contain digits, spaces, +, -, and parentheses only.");
        }

        var normalizedRegion = NormalizeRegionCode(regionCode);

        try
        {
            var parsed = _phoneNumberUtil.Parse(rawValue, normalizedRegion);
            if (!_phoneNumberUtil.IsValidNumber(parsed))
            {
                return Invalid("Please enter a valid phone number for the selected country.");
            }

            var numberType = _phoneNumberUtil.GetNumberType(parsed);
            if (numberType is PhoneNumberType.UNKNOWN)
            {
                return Invalid("Please enter a valid phone number.");
            }

            var e164 = _phoneNumberUtil.Format(parsed, PhoneNumberFormat.E164);
            var nationalNumberLength = parsed.NationalNumber.ToString().Length;
            if (nationalNumberLength is < 4 or > 15)
            {
                return Invalid("Phone number length is not valid for the selected country.");
            }

            var parsedRegion = _phoneNumberUtil.GetRegionCodeForNumber(parsed);
            return new PhoneNumberValidationResult(true, e164, parsedRegion, null);
        }
        catch (NumberParseException)
        {
            return Invalid("Please enter a valid phone number.");
        }
    }

    private static PhoneNumberValidationResult Invalid(string errorMessage)
        => new(false, null, null, errorMessage);

    private static string? NormalizeRegionCode(string? regionCode)
    {
        if (string.IsNullOrWhiteSpace(regionCode))
        {
            return null;
        }

        var normalized = regionCode.Trim().ToUpperInvariant();
        return normalized.Length == 2 ? normalized : null;
    }
}
