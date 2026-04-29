using ECommerce_System.Resources;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace ECommerce_System.Utilities.Localization;

public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> localizer)
    {
        _localizer = localizer;
    }

    public override IdentityError DefaultError()
        => Build(nameof(DefaultError), _localizer["DefaultError"]);

    public override IdentityError ConcurrencyFailure()
        => Build(nameof(ConcurrencyFailure), _localizer["ConcurrencyFailure"]);

    public override IdentityError PasswordMismatch()
        => Build(nameof(PasswordMismatch), _localizer["PasswordMismatch"]);

    public override IdentityError InvalidToken()
        => Build(nameof(InvalidToken), _localizer["InvalidToken"]);

    public override IdentityError LoginAlreadyAssociated()
        => Build(nameof(LoginAlreadyAssociated), _localizer["LoginAlreadyAssociated"]);

    public override IdentityError InvalidUserName(string? userName)
        => Build(nameof(InvalidUserName), _localizer["InvalidUserName", userName ?? string.Empty]);

    public override IdentityError InvalidEmail(string? email)
        => Build(nameof(InvalidEmail), _localizer["InvalidEmail", email ?? string.Empty]);

    public override IdentityError DuplicateUserName(string userName)
        => Build(nameof(DuplicateUserName), _localizer["DuplicateUserName", userName]);

    public override IdentityError DuplicateEmail(string email)
        => Build(nameof(DuplicateEmail), _localizer["DuplicateEmail", email]);

    public override IdentityError InvalidRoleName(string? role)
        => Build(nameof(InvalidRoleName), _localizer["InvalidRoleName", role ?? string.Empty]);

    public override IdentityError DuplicateRoleName(string role)
        => Build(nameof(DuplicateRoleName), _localizer["DuplicateRoleName", role]);

    public override IdentityError UserAlreadyHasPassword()
        => Build(nameof(UserAlreadyHasPassword), _localizer["UserAlreadyHasPassword"]);

    public override IdentityError UserLockoutNotEnabled()
        => Build(nameof(UserLockoutNotEnabled), _localizer["UserLockoutNotEnabled"]);

    public override IdentityError UserAlreadyInRole(string role)
        => Build(nameof(UserAlreadyInRole), _localizer["UserAlreadyInRole", role]);

    public override IdentityError UserNotInRole(string role)
        => Build(nameof(UserNotInRole), _localizer["UserNotInRole", role]);

    public override IdentityError PasswordTooShort(int length)
        => Build(nameof(PasswordTooShort), _localizer["PasswordTooShort", length]);

    public override IdentityError PasswordRequiresNonAlphanumeric()
        => Build(nameof(PasswordRequiresNonAlphanumeric), _localizer["PasswordRequiresNonAlphanumeric"]);

    public override IdentityError PasswordRequiresDigit()
        => Build(nameof(PasswordRequiresDigit), _localizer["PasswordRequiresDigit"]);

    public override IdentityError PasswordRequiresLower()
        => Build(nameof(PasswordRequiresLower), _localizer["PasswordRequiresLower"]);

    public override IdentityError PasswordRequiresUpper()
        => Build(nameof(PasswordRequiresUpper), _localizer["PasswordRequiresUpper"]);

    public override IdentityError RecoveryCodeRedemptionFailed()
        => Build(nameof(RecoveryCodeRedemptionFailed), _localizer["RecoveryCodeRedemptionFailed"]);

    private static IdentityError Build(string code, string description)
        => new() { Code = code, Description = description };
}
