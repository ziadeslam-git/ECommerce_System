using ECommerce_System.Models;
using ECommerce_System.Resources;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ECommerce_System.Areas.Identity.Controllers;

[Area("Identity")]
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailSender _emailSender;
    private readonly IStringLocalizer<SharedResource> _localizer;

    // ✅ FIX 1: Removed IRepository<ApplicationUserOTP> — لا وجود لـ ApplicationUserOTP في هذا المشروع
    // ✅ FIX 2: Removed wrong using (Microsoft.VisualStudio.Web.CodeGenerators...)
    // ✅ FIX 3: Constructor نظيف بدون OTP dependency
    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender emailSender,
        IStringLocalizer<SharedResource> localizer)
    {
        _userManager  = userManager;
        _signInManager = signInManager;
        _emailSender  = emailSender;
        _localizer = localizer;
    }

    // ─── LOGOUT ─────────────────────────────────────────────────
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    // ─── REGISTER ───────────────────────────────────────────────
    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // ✅ FIX 4: استخدمنا FullName بدل Name (ApplicationUser في هذا المشروع ليه FullName)
        // ✅ FIX 5: حذفنا user.Address — الـ Addresses موجودة في جدول منفصل
        var user = new ApplicationUser
        {
            FullName       = vm.FullName,
            Email          = vm.Email,
            UserName       = vm.Email,  // Username = Email (standard)
            CreatedAt      = DateTime.UtcNow,
            IsActive       = true,
        };

        var result = await _userManager.CreateAsync(user, vm.Password);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(vm);
        }

        // ✅ FIX 6: استخدمنا SD.Role_Customer بدل SD.CUSTOMER_ROLE
        await _userManager.AddToRoleAsync(user, SD.Role_Customer);

        // Send email confirmation link
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var link  = Url.Action(nameof(ConfirmEmail), "Account",
                        new { area = "Identity", token, email = user.Email }, Request.Scheme);

        var confirmEmailBody = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Confirm Your Email</title>
            </head>
            <body style="margin:0; padding:0; background-color:#f4f4f7; font-family:'Segoe UI', Arial, sans-serif;">
                <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 0;">
                    <tr><td align="center">
                        <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff; border-radius:16px; overflow:hidden; box-shadow: 0 4px 24px rgba(0,0,0,0.08);">
                            <!-- Header -->
                            <tr><td style="background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); padding:32px 40px; text-align:center;">
                                <h1 style="margin:0; color:#ffffff; font-size:26px; font-weight:700; letter-spacing:-0.5px;">🛍️ Smart Store</h1>
                                <p style="margin:8px 0 0; color:rgba(255,255,255,0.8); font-size:14px;">Welcome to the family!</p>
                            </td></tr>
                            <!-- Icon -->
                            <tr><td style="padding:32px 40px 0; text-align:center;">
                                <div style="width:72px; height:72px; background:#ede9fe; border-radius:50%; line-height:72px; font-size:32px; text-align:center; display:inline-block;">✅</div>
                            </td></tr>
                            <!-- Body -->
                            <tr><td style="padding:24px 40px 32px; text-align:center;">
                                <h2 style="margin:0 0 12px; color:#1e1b4b; font-size:22px; font-weight:700;">Confirm Your Email</h2>
                                <p style="margin:0 0 24px; color:#6b7280; font-size:15px; line-height:1.7;">Thank you for joining Smart Store, {user.FullName}! Please confirm your email address to activate your account.</p>
                                <a href="{link}" style="display:inline-block; padding:14px 36px; background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); color:#ffffff; text-decoration:none; border-radius:10px; font-size:15px; font-weight:600; letter-spacing:0.3px;">Confirm My Email</a>
                            </td></tr>
                            <!-- Footer -->
                            <tr><td style="padding:0 40px;"><hr style="border:none; border-top:1px solid #e5e7eb; margin:0;" /></td></tr>
                            <tr><td style="padding:20px 40px 28px;">
                                <p style="margin:0; color:#9ca3af; font-size:12px; text-align:center; line-height:1.7;">If you didn't create a Smart Store account, please ignore this email.<br/>© 2026 Smart Store. All rights reserved.</p>
                            </td></tr>
                        </table>
                    </td></tr>
                </table>
            </body>
            </html>
            """;

        await _emailSender.SendEmailAsync(user.Email!,
            "Smart Store — Confirm Your Email",
            confirmEmailBody);

        TempData["success"] = _localizer["AccountCreatedCheckEmail"].Value;
        return RedirectToAction(nameof(Login));
    }

    // ─── CONFIRM EMAIL ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string token, string email)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null) return NotFound();

        var result = await _userManager.ConfirmEmailAsync(user, token);

        if (result.Succeeded)
            TempData["success"] = _localizer["EmailConfirmedSuccess"].Value;
        else
            TempData["error"] = _localizer["EmailConfirmationFailed"].Value;

        return View();
    }

    // ─── LOGIN ──────────────────────────────────────────────────
    [HttpGet]
    public IActionResult Login() => View();

    [HttpGet]
    public IActionResult AccessDenied() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, _localizer["InvalidEmailOrPassword"]);
            return View(vm);
        }

        if (!user.IsActive)
        {
            return RedirectToAction(nameof(AccountDeactivated));
        }

        var result = await _signInManager.PasswordSignInAsync(
            user, vm.Password,
            isPersistent: vm.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
            return RedirectToAction("Index", "Home", new { area = "Customer" });

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, _localizer["AccountLockedOut"]);
        else if (result.IsNotAllowed)
            ModelState.AddModelError(string.Empty, _localizer["LoginNotAllowed"]);
        else
            ModelState.AddModelError(string.Empty, _localizer["InvalidEmailOrPassword"]);

        return View(vm);
    }

    // ─── ACCOUNT DEACTIVATED ─────────────────────────────────
    [HttpGet]
    public IActionResult AccountDeactivated() => View();


    // ─── FORGOT PASSWORD ────────────────────────────────────────
    [HttpGet]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);

        // ✅ Always show same message for security (don't leak whether email exists)
        // ✅ FIX: Removed IsEmailConfirmedAsync gate — unconfirmed users were permanently
        //    locked out of password reset, creating a deadlock.
        if (user is not null)
        {
            // ✅ FIX 7: استخدمنا GeneratePasswordResetTokenAsync (Identity standard)
            // بدل OTP system القديمة اللي كانت بتحتاج ApplicationUserOTP من DB
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var link  = Url.Action(nameof(ResetPassword), "Account",
                            new { area = "Identity", token, email = user.Email }, Request.Scheme);

            var emailBody = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="UTF-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                    <title>Reset Your Password</title>
                </head>
                <body style="margin:0; padding:0; background-color:#f4f4f7; font-family:'Segoe UI', Arial, sans-serif;">
                    <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 0;">
                        <tr><td align="center">
                            <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff; border-radius:16px; overflow:hidden; box-shadow: 0 4px 24px rgba(0,0,0,0.08);">
                                <!-- Header -->
                                <tr><td style="background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); padding:32px 40px; text-align:center;">
                                    <h1 style="margin:0; color:#ffffff; font-size:26px; font-weight:700; letter-spacing:-0.5px;">🛍️ Smart Store</h1>
                                    <p style="margin:8px 0 0; color:rgba(255,255,255,0.8); font-size:14px;">Your trusted shopping destination</p>
                                </td></tr>
                                <!-- Icon -->
                                <tr><td style="padding:32px 40px 0; text-align:center;">
                                    <div style="width:72px; height:72px; background:#ede9fe; border-radius:50%; line-height:72px; font-size:32px; text-align:center; display:inline-block;">🔐</div>
                                </td></tr>
                                <!-- Body -->
                                <tr><td style="padding:24px 40px 32px; text-align:center;">
                                    <h2 style="margin:0 0 12px; color:#1e1b4b; font-size:22px; font-weight:700;">Reset Your Password</h2>
                                    <p style="margin:0 0 24px; color:#6b7280; font-size:15px; line-height:1.7;">We received a request to reset the password for your Smart Store account. Click the button below to set a new password.</p>
                                    <a href="{link}" style="display:inline-block; padding:14px 36px; background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); color:#ffffff; text-decoration:none; border-radius:10px; font-size:15px; font-weight:600; letter-spacing:0.3px;">Reset My Password</a>
                                    <p style="margin:24px 0 0; color:#9ca3af; font-size:13px;">⏱️ This link expires in <strong>1 hour</strong>.</p>
                                </td></tr>
                                <!-- Footer -->
                                <tr><td style="padding:0 40px;"><hr style="border:none; border-top:1px solid #e5e7eb; margin:0;" /></td></tr>
                                <tr><td style="padding:20px 40px 28px;">
                                    <p style="margin:0; color:#9ca3af; font-size:12px; text-align:center; line-height:1.7;">If you didn't request a password reset, you can safely ignore this email. Your password will not be changed.<br/>© 2026 Smart Store. All rights reserved.</p>
                                </td></tr>
                            </table>
                        </td></tr>
                    </table>
                </body>
                </html>
                """;

            await _emailSender.SendEmailAsync(
                user.Email!,
                "Smart Store — Reset Your Password",
                emailBody);
        }

        TempData["success"] = _localizer["IfEmailExistsResetSent"].Value;
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    // ─── RESET PASSWORD ─────────────────────────────────────────
    [HttpGet]
    public IActionResult ResetPassword(string token, string email)
        => View(new ResetPasswordVM { Token = token, Email = email });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user is null)
        {
            TempData["success"] = _localizer["PasswordResetSuccess"].Value;
            return RedirectToAction(nameof(Login));
        }

        var result = await _userManager.ResetPasswordAsync(user, vm.Token, vm.NewPassword);

        if (result.Succeeded)
        {
            TempData["success"] = _localizer["PasswordResetPleaseLogin"].Value;
            return RedirectToAction(nameof(Login));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(vm);
    }

    // ─── RESEND CONFIRMATION EMAIL ──────────────────────────────
    [HttpGet]
    public IActionResult ResendConfirmation() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendConfirmation(ForgotPasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.FindByEmailAsync(vm.Email);

        // Security: always show the same message regardless of whether the email exists
        if (user is not null && !await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var link  = Url.Action(nameof(ConfirmEmail), "Account",
                            new { area = "Identity", token, email = user.Email }, Request.Scheme);

            var confirmEmailBody = $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="UTF-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                    <title>Confirm Your Email</title>
                </head>
                <body style="margin:0; padding:0; background-color:#f4f4f7; font-family:'Segoe UI', Arial, sans-serif;">
                    <table width="100%" cellpadding="0" cellspacing="0" style="padding:40px 0;">
                        <tr><td align="center">
                            <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff; border-radius:16px; overflow:hidden; box-shadow: 0 4px 24px rgba(0,0,0,0.08);">
                                <tr><td style="background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); padding:32px 40px; text-align:center;">
                                    <h1 style="margin:0; color:#ffffff; font-size:26px; font-weight:700;">🛍️ Smart Store</h1>
                                    <p style="margin:8px 0 0; color:rgba(255,255,255,0.8); font-size:14px;">Welcome to the family!</p>
                                </td></tr>
                                <tr><td style="padding:32px 40px 0; text-align:center;">
                                    <div style="width:72px; height:72px; background:#ede9fe; border-radius:50%; line-height:72px; font-size:32px; text-align:center; display:inline-block;">✅</div>
                                </td></tr>
                                <tr><td style="padding:24px 40px 32px; text-align:center;">
                                    <h2 style="margin:0 0 12px; color:#1e1b4b; font-size:22px; font-weight:700;">Confirm Your Email</h2>
                                    <p style="margin:0 0 24px; color:#6b7280; font-size:15px; line-height:1.7;">Please confirm your email address to activate your Smart Store account.</p>
                                    <a href="{link}" style="display:inline-block; padding:14px 36px; background: linear-gradient(135deg, #6366f1 0%, #4f46e5 100%); color:#ffffff; text-decoration:none; border-radius:10px; font-size:15px; font-weight:600; letter-spacing:0.3px;">Confirm My Email</a>
                                </td></tr>
                                <tr><td style="padding:0 40px;"><hr style="border:none; border-top:1px solid #e5e7eb; margin:0;" /></td></tr>
                                <tr><td style="padding:20px 40px 28px;">
                                    <p style="margin:0; color:#9ca3af; font-size:12px; text-align:center; line-height:1.7;">If you didn't create a Smart Store account, please ignore this email.<br/>© 2026 Smart Store. All rights reserved.</p>
                                </td></tr>
                            </table>
                        </td></tr>
                    </table>
                </body>
                </html>
                """;

            await _emailSender.SendEmailAsync(
                user.Email!,
                "Smart Store — Confirm Your Email",
                confirmEmailBody);
        }

        TempData["success"] = _localizer["IfEmailExistsConfirmationSent"].Value;
        return RedirectToAction(nameof(Login));
    }
}
