using ECommerce_System.Models;
using ECommerce_System.Resources;
using ECommerce_System.Utilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Security.Claims;

namespace ECommerce_System.Areas.Identity.Controllers;

[Area("Identity")]
public class ExternalLoginController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ExternalLoginController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _localizer = localizer;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        var redirectUrl = Url.Action(nameof(Callback), "ExternalLogin", new { ReturnUrl = returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet]
    public async Task<IActionResult> Callback(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = returnUrl ?? Url.Content("~/");
        if (remoteError != null)
        {
            TempData["error"] = _localizer["ExternalProviderError", remoteError].Value;
            return RedirectToAction("Login", "Account");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            TempData["error"] = _localizer["ExternalLoginInfoLoadError"].Value;
            return RedirectToAction("Login", "Account");
        }

        // Sign in the user with this external login provider if the user already has a login.
        var result = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: false);
        if (result.Succeeded)
        {
            await _signInManager.UpdateExternalAuthenticationTokensAsync(info);
            return LocalRedirect(returnUrl);
        }
        if (result.IsLockedOut)
        {
            TempData["error"] = _localizer["ExternalAccountLockedOut"].Value;
            return RedirectToAction("Login", "Account");
        }
        else
        {
            // If the user does not have an account, then ask the user to create an account.
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["ProviderDisplayName"] = info.ProviderDisplayName;
            var email = info.Principal.FindFirstValue(ClaimTypes.Email);
            var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? _localizer["ExternalUser"].Value;
            
            // If email is null (e.g. from Facebook when we don't request the email scope),
            // leave it empty so the user can fill it in on the Callback view.
            return View("Callback", new ExternalLoginConfirmationVM { Email = email ?? "", FullName = name });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirmation(ExternalLoginConfirmationVM model, string? returnUrl = null)
    {
        returnUrl = returnUrl ?? Url.Content("~/");

        if (ModelState.IsValid)
        {
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                TempData["error"] = _localizer["ExternalLoginInfoConfirmLoadError"].Value;
                return RedirectToAction("Login", "Account", new { ReturnUrl = returnUrl });
            }

            var user = new ApplicationUser { UserName = model.Email, Email = model.Email, FullName = model.FullName, PhoneNumber = model.PhoneNumber, IsActive = true, EmailConfirmed = true, CreatedAt = DateTime.UtcNow };

            var result = await _userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                result = await _userManager.AddLoginAsync(user, info);
                if (result.Succeeded)
                {
                    // ✅ FIX: Assign Customer role to external login users
                    await _userManager.AddToRoleAsync(user, SD.Role_Customer);
                    await _signInManager.SignInAsync(user, isPersistent: true, info.LoginProvider);
                    return LocalRedirect(returnUrl);
                }
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View("Callback", model);
    }
}

public class ExternalLoginConfirmationVM
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; } // Phone number can be nullable in identity by default
}
