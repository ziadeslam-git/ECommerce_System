using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Resources;
using ECommerce_System.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_AdminOrCustomer)]
public class ReviewsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ReviewsController(IUnitOfWork unitOfWork, IStringLocalizer<SharedResource> localizer)
    {
        _unitOfWork = unitOfWork;
        _localizer = localizer;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int productId, int rating, string? comment)
    {
        if (rating < 1 || rating > 5)
        {
            TempData["error"] = _localizer["RatingMustBeBetweenOneAndFive"].Value;
            return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
        }

        var product = await _unitOfWork.Products.FindAsync(p => p.Id == productId && p.IsActive, tracked: false);
        if (product is null)
        {
            TempData["error"] = _localizer["ProductNotFound"].Value;
            return RedirectToAction("Index", "Products", new { area = "Customer" });
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction("Login", "Account", new { area = "Identity" });
        }

        var hasPurchasedDelivered = (await _unitOfWork.Orders
            .FindAllAsync(o => o.UserId == userId && o.Status == SD.Status_Delivered, "OrderItems.ProductVariant", tracked: false))
            .Any(o => o.OrderItems.Any(oi => oi.ProductVariant.ProductId == productId));

        if (!hasPurchasedDelivered)
        {
            TempData["error"] = _localizer["ReviewOnlyDeliveredPurchased"].Value;
            return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
        }

        var existingReview = await _unitOfWork.Reviews
            .FindAsync(r => r.UserId == userId && r.ProductId == productId, tracked: false, ignoreQueryFilters: true);
        if (existingReview is not null)
        {
            TempData["error"] = _localizer["ReviewAlreadySubmitted"].Value;
            return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
        }

        await _unitOfWork.Reviews.AddAsync(new Models.Review
        {
            UserId = userId,
            ProductId = productId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            IsApproved = false,
            IsRejected = false
        });

        await _unitOfWork.SaveAsync();

        TempData["success"] = _localizer["ReviewSubmittedPendingApproval"].Value;
        return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
    }
}

