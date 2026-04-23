using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce_System.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize(Roles = SD.Role_Customer)]
public class ReviewsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;

    public ReviewsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int productId, int rating, string? comment)
    {
        if (rating < 1 || rating > 5)
        {
            TempData["error"] = "Rating must be between 1 and 5.";
            return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
        }

        var product = await _unitOfWork.Products.FindAsync(p => p.Id == productId && p.IsActive, tracked: false);
        if (product is null)
        {
            TempData["error"] = "Product not found.";
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
            TempData["error"] = "You can review only delivered products you purchased.";
            return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
        }

        var existingReview = await _unitOfWork.Reviews
            .FindAsync(r => r.UserId == userId && r.ProductId == productId, tracked: false, ignoreQueryFilters: true);
        if (existingReview is not null)
        {
            TempData["error"] = "You already submitted a review for this product.";
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

        TempData["success"] = "Review submitted and pending admin approval.";
        return RedirectToAction("Details", "Products", new { area = "Customer", id = productId });
    }
}

