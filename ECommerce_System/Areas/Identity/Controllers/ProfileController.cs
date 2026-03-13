using ECommerce_System.Models;
using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.ViewModels.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Identity.Controllers;

[Area("Identity")]
[Authorize]
public class ProfileController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUnitOfWork _unitOfWork;

    // ✅ FIX: Added IUnitOfWork for Address management
    public ProfileController(UserManager<ApplicationUser> userManager, IUnitOfWork unitOfWork)
    {
        _userManager = userManager;
        _unitOfWork  = unitOfWork;
    }

    // ─── PROFILE INDEX ──────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        // ✅ FIX: استخدمنا ProfileVM بدل ApplicationUserVM اللي مش موجودة
        // ✅ FIX: استخدمنا FullName بدل Name
        var vm = new ProfileVM
        {
            FullName    = user.FullName,
            Email       = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
        };

        return View(vm);
    }

    // ─── UPDATE PROFILE ─────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        // ✅ FIX: FullName بدل Name / مفيش user.Address
        user.FullName   = vm.FullName;
        user.PhoneNumber = vm.PhoneNumber;

        var result = await _userManager.UpdateAsync(user);

        if (result.Succeeded)
            TempData["success"] = "Profile updated successfully.";
        else
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

        return View(vm);
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
