using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Identity.Controllers;

[Area("Identity")]
[Authorize]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICloudinaryService _cloudinaryService;

    // ✅ FIX: Added IUnitOfWork for Address management
    public ProfileController(
        UserManager<ApplicationUser> userManager,
        IUnitOfWork unitOfWork,
        ICloudinaryService cloudinaryService)
    {
        _userManager = userManager;
        _unitOfWork  = unitOfWork;
        _cloudinaryService = cloudinaryService;
    }

    // ─── PROFILE INDEX ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var (phoneCountryCode, phoneNumber) = SplitPhoneNumber(user.PhoneNumber);

        var vm = new ProfileVM
        {
            FullName    = user.FullName,
            Email       = user.Email ?? string.Empty,
            PhoneNumber = phoneNumber,
            PhoneCountryCode = phoneCountryCode,
            ProfileImageUrl = user.ProfileImageUrl
        };

        return View(vm);
    }

    // ─── UPDATE PROFILE ─────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileVM vm)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        vm.Email ??= user.Email ?? string.Empty;
        vm.ProfileImageUrl ??= user.ProfileImageUrl;
        var hasCroppedProfileImage = !string.IsNullOrWhiteSpace(vm.CroppedProfileImageDataUrl);

        if (vm.ProfileImage is not null && vm.ProfileImage.Length > 0 && !hasCroppedProfileImage)
        {
            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(vm.ProfileImage.ContentType.ToLowerInvariant()))
            {
                ModelState.AddModelError(nameof(vm.ProfileImage), "Only JPG, PNG and WebP images are allowed.");
            }

            if (vm.ProfileImage.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(vm.ProfileImage), "Profile image must not exceed 5 MB.");
            }
        }

        if (hasCroppedProfileImage && !TryBuildCroppedImageFile(vm.CroppedProfileImageDataUrl!, out _, out var cropError))
        {
            ModelState.AddModelError(nameof(vm.ProfileImage), cropError ?? "The cropped image could not be processed. Please choose the photo again.");
        }

        if (!ModelState.IsValid) return View(vm);

        // ✅ FIX: FullName بدل Name / مفيش user.Address
        user.FullName   = vm.FullName;
        user.PhoneNumber = BuildPhoneNumber(vm.PhoneCountryCode, vm.PhoneNumber);

        string? oldPublicId = null;
        string? uploadedPublicId = null;

        if (hasCroppedProfileImage)
        {
            TryBuildCroppedImageFile(vm.CroppedProfileImageDataUrl!, out var croppedFile, out _);
            if (croppedFile is not null)
            {
                var uploadResult = await _cloudinaryService.UploadAsync(croppedFile, SD.Cloudinary_ProfileFolder);
                oldPublicId = user.ProfileImagePublicId;
                uploadedPublicId = uploadResult.PublicId;
                user.ProfileImageUrl = uploadResult.Url;
                user.ProfileImagePublicId = uploadResult.PublicId;
            }
        }
        else if (vm.ProfileImage is not null && vm.ProfileImage.Length > 0)
        {
            var uploadResult = await _cloudinaryService.UploadAsync(vm.ProfileImage, SD.Cloudinary_ProfileFolder);
            oldPublicId = user.ProfileImagePublicId;
            uploadedPublicId = uploadResult.PublicId;
            user.ProfileImageUrl = uploadResult.Url;
            user.ProfileImagePublicId = uploadResult.PublicId;
        }

        var result = await _userManager.UpdateAsync(user);

        if (result.Succeeded)
        {
            if (!string.IsNullOrWhiteSpace(oldPublicId) && oldPublicId != uploadedPublicId)
            {
                await _cloudinaryService.DeleteAsync(oldPublicId);
            }

            TempData["success"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Index));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(uploadedPublicId))
            {
                await _cloudinaryService.DeleteAsync(uploadedPublicId);
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
        }

        vm.ProfileImageUrl = user.ProfileImageUrl;
        return View(vm);
    }

    private static (string CountryCode, string? LocalNumber) SplitPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return ("+20", string.Empty);

        var normalized = phoneNumber.Trim();
        var knownCodes = new[] { "+20", "+1", "+44" };

        foreach (var code in knownCodes.OrderByDescending(c => c.Length))
        {
            if (normalized.StartsWith(code, StringComparison.Ordinal))
            {
                return (code, normalized[code.Length..].TrimStart());
            }
        }

        return ("+20", normalized);
    }

    private static string? BuildPhoneNumber(string? countryCode, string? localNumber)
    {
        var code = string.IsNullOrWhiteSpace(countryCode) ? "+20" : countryCode.Trim();
        var local = string.IsNullOrWhiteSpace(localNumber) ? string.Empty : localNumber.Trim();

        if (string.IsNullOrWhiteSpace(local))
            return null;

        return $"{code}{local}";
    }

    private static bool TryBuildCroppedImageFile(
        string dataUrl,
        out IFormFile? formFile,
        out string? errorMessage)
    {
        formFile = null;
        errorMessage = null;

        var parts = dataUrl.Split(',', 2);
        if (parts.Length != 2 || !parts[0].StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Invalid cropped image format.";
            return false;
        }

        string contentType;
        string extension;

        if (parts[0].Contains("image/png", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "image/png";
            extension = ".png";
        }
        else if (parts[0].Contains("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "image/webp";
            extension = ".webp";
        }
        else
        {
            contentType = "image/jpeg";
            extension = ".jpg";
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            errorMessage = "The cropped image data is invalid.";
            return false;
        }

        if (bytes.Length == 0)
        {
            errorMessage = "The cropped image is empty.";
            return false;
        }

        if (bytes.Length > 5 * 1024 * 1024)
        {
            errorMessage = "Profile image must not exceed 5 MB.";
            return false;
        }

        var stream = new MemoryStream(bytes);
        formFile = new FormFile(stream, 0, bytes.Length, "ProfileImage", $"profile-crop{extension}")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        return true;
    }

    // ─── CHANGE PASSWORD ────────────────────────────────────────
    [HttpGet]
    public IActionResult ChangePassword() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, vm.CurrentPassword, vm.NewPassword);

        if (result.Succeeded)
        {
            TempData["success"] = "Password changed successfully.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        return View(vm);
    }

    // ─── ADDRESSES ──────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Addresses()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var addresses = await _unitOfWork.Addresses
            .FindAllAsync(a => a.UserId == user.Id);

        return View(addresses);
    }

    [HttpGet]
    public IActionResult AddAddress() => View(new AddressVM());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAddress(AddressVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        // If this is the first/default address, clear existing defaults
        if (vm.IsDefault)
        {
            var existingDefaults = await _unitOfWork.Addresses
                .FindAllAsync(a => a.UserId == user.Id && a.IsDefault);
            foreach (var addr in existingDefaults)
            {
                addr.IsDefault = false;
                _unitOfWork.Addresses.Update(addr);
            }
        }

        var address = new Address
        {
            UserId      = user.Id,
            FullName    = vm.FullName,
            PhoneNumber = vm.PhoneNumber,
            Street      = vm.Street,
            City        = vm.City,
            State       = vm.State,
            Country     = vm.Country,
            PostalCode  = vm.PostalCode,
            IsDefault   = vm.IsDefault,
        };

        await _unitOfWork.Addresses.AddAsync(address);
        await _unitOfWork.SaveAsync();

        TempData["success"] = "Address added successfully.";
        return RedirectToAction(nameof(Addresses));
    }

    [HttpGet]
    public async Task<IActionResult> EditAddress(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var address = await _unitOfWork.Addresses.GetByIdAsync(id);
        if (address is null || address.UserId != user.Id) return NotFound();

        var vm = new AddressVM
        {
            Id          = address.Id,
            FullName    = address.FullName,
            PhoneNumber = address.PhoneNumber,
            Street      = address.Street,
            City        = address.City,
            State       = address.State,
            Country     = address.Country,
            PostalCode  = address.PostalCode,
            IsDefault   = address.IsDefault,
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAddress(AddressVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var address = await _unitOfWork.Addresses.GetByIdAsync(vm.Id);
        if (address is null || address.UserId != user.Id) return NotFound();

        if (vm.IsDefault)
        {
            var existingDefaults = await _unitOfWork.Addresses
                .FindAllAsync(a => a.UserId == user.Id && a.IsDefault && a.Id != vm.Id);
            foreach (var addr in existingDefaults)
            {
                addr.IsDefault = false;
                _unitOfWork.Addresses.Update(addr);
            }
        }

        address.FullName    = vm.FullName;
        address.PhoneNumber = vm.PhoneNumber;
        address.Street      = vm.Street;
        address.City        = vm.City;
        address.State       = vm.State;
        address.Country     = vm.Country;
        address.PostalCode  = vm.PostalCode;
        address.IsDefault   = vm.IsDefault;

        _unitOfWork.Addresses.Update(address);
        await _unitOfWork.SaveAsync();

        TempData["success"] = "Address updated successfully.";
        return RedirectToAction(nameof(Addresses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAddress(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var address = await _unitOfWork.Addresses.GetByIdAsync(id);
        if (address is null || address.UserId != user.Id) return NotFound();

        _unitOfWork.Addresses.Remove(address);
        await _unitOfWork.SaveAsync();

        TempData["success"] = "Address deleted.";
        return RedirectToAction(nameof(Addresses));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefaultAddress(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        var allAddresses = await _unitOfWork.Addresses.FindAllAsync(a => a.UserId == user.Id);
        foreach (var addr in allAddresses)
        {
            addr.IsDefault = addr.Id == id;
            _unitOfWork.Addresses.Update(addr);
        }

        await _unitOfWork.SaveAsync();
        TempData["Success"] = "Default address updated successfully.";
        return RedirectToAction(nameof(Addresses));
    }
}
