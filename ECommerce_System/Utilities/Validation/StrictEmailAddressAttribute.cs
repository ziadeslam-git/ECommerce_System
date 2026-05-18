using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ECommerce_System.Utilities.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed partial class StrictEmailAddressAttribute : ValidationAttribute, IClientModelValidator
{
    private const string Pattern = @"^[A-Za-z0-9](?:[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]{0,62}[A-Za-z0-9])?@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$";

    public StrictEmailAddressAttribute()
    {
        ErrorMessage = "Please enter a valid email address, for example example@gmail.com.";
    }

    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        var email = Convert.ToString(value)?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        if (email.Length > 254 || email.Contains(' ') || email.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = email.Split('@');
        if (parts.Length != 2 || parts[0].Length is 0 or > 64 || parts[1].Length is 0 or > 253)
        {
            return false;
        }

        return EmailRegex().IsMatch(email);
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MergeAttribute(context.Attributes, "data-val", "true");
        MergeAttribute(context.Attributes, "data-val-regex", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        MergeAttribute(context.Attributes, "data-val-regex-pattern", Pattern);
    }

    private static bool MergeAttribute(IDictionary<string, string> attributes, string key, string value)
    {
        if (attributes.ContainsKey(key))
        {
            return false;
        }

        attributes.Add(key, value);
        return true;
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
