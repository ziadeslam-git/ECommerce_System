using ECommerce_System.Repositories.IRepositories;
using ECommerce_System.Utilities;
using ECommerce_System.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ECommerce_System.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SD.Role_Admin)]
public class ReviewsController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    private const int PageSize = 10;

    public ReviewsController(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    // ──────────────────────────────────────────────────────────
    // GET: /Admin/Reviews?status=Pending|Approved|Rejected
    //  FIX: server-side pagination + correct Rejected filter
    // ──────────────────────────────────────────────────────────
    public async Task<IActionResult> Index(string? status = null, int page = 1)
    {
        page = Math.Max(page, 1);

        var allReviewsQuery = _unitOfWork.Reviews.Query().AsNoTracking();
        ViewBag.AllCount = await allReviewsQuery.CountAsync();
        ViewBag.PendingCount = await allReviewsQuery.CountAsync(r => !r.IsApproved && !r.IsRejected);
        ViewBag.ApprovedCount = await allReviewsQuery.CountAsync(r => r.IsApproved);
        ViewBag.RejectedCount = await allReviewsQuery.CountAsync(r => r.IsRejected);

        var filteredQuery = _unitOfWork.Reviews.Query()
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Product)
            .AsQueryable();

        filteredQuery = (status ?? "").ToLower() switch
        {
            "approved" => filteredQuery.Where(r => r.IsApproved),
            "rejected" => filteredQuery.Where(r => r.IsRejected),
            "pending" => filteredQuery.Where(r => !r.IsApproved && !r.IsRejected),
            _ => filteredQuery
        };

        var total = await filteredQuery.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        if (page > totalPages)
            page = totalPages;

        var items = await filteredQuery
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var vms = items
            .Select(r => new ReviewVM
            {
                Id           = r.Id,
                UserId       = r.UserId,
                UserFullName = r.User?.FullName ?? "Unknown",
                UserEmail    = r.User?.Email    ?? string.Empty,
                UserName     = r.User?.FullName ?? "Unknown",
                ProductId    = r.ProductId,
                ProductName  = r.Product?.Name ?? "Unknown",
                Rating       = r.Rating,
                Comment      = r.Comment,
                IsApproved   = r.IsApproved,
                IsRejected   = r.IsRejected,        // FIX: map real field
                CreatedAt    = r.CreatedAt           // FIX: use actual DB date
            })
            .ToList();

        ViewBag.StatusFilter = status;
        ViewBag.CurrentPage  = page;
        ViewBag.TotalPages   = totalPages;
        ViewBag.TotalCount   = total;
        ViewBag.PageSize     = PageSize;
        ViewData["Title"]    = "Reviews";
        return View(vms);
    }

    // GET: /Admin/Reviews/Details/5
    public async Task<IActionResult> Details(int id)
    {
        var review = await _unitOfWork.Reviews
            .FindAsync(r => r.Id == id, "User,Product");

        if (review == null) return NotFound();

        var vm = new ReviewVM
        {
            Id           = review.Id,
            UserId       = review.UserId,
            UserFullName = review.User?.FullName ?? "Unknown",
            UserEmail    = review.User?.Email    ?? string.Empty,
            UserName     = review.User?.FullName ?? "Unknown",
            ProductId    = review.ProductId,
            ProductName  = review.Product?.Name ?? "Unknown",
            Rating       = review.Rating,
            Comment      = review.Comment,
            IsApproved   = review.IsApproved,
            IsRejected   = review.IsRejected,
            CreatedAt    = review.CreatedAt
        };

        ViewData["Title"] = "Review Details";
        return View(vm);
    }

    // POST: /Admin/Reviews/Approve/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var review = await _unitOfWork.Reviews.GetByIdAsync(id);
        if (review == null) return NotFound();

        review.IsApproved = true;
        review.IsRejected = false; // clear any previous rejection
        _unitOfWork.Reviews.Update(review);
        await _unitOfWork.SaveAsync();

        await RecalculateAverageRatingAsync(review.ProductId);

        TempData["success"] = "Review approved successfully.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Admin/Reviews/Reject/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var review = await _unitOfWork.Reviews.GetByIdAsync(id);
        if (review == null) return NotFound();

        review.IsApproved = false;
        review.IsRejected = true;  // FIX: set the correct flag
        _unitOfWork.Reviews.Update(review);
        await _unitOfWork.SaveAsync();

        await RecalculateAverageRatingAsync(review.ProductId);

        TempData["success"] = "Review rejected successfully.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Admin/Reviews/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var review = await _unitOfWork.Reviews.GetByIdAsync(id);
        if (review == null) return NotFound();

        int productId = review.ProductId;

        _unitOfWork.Reviews.Remove(review);
        await _unitOfWork.SaveAsync();

        await RecalculateAverageRatingAsync(productId);

        TempData["success"] = "Review deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Private Helper ───────────────────────────────────────────────────────
    private async Task RecalculateAverageRatingAsync(int productId)
    {
        var approvedReviews = await _unitOfWork.Reviews
            .FindAllAsync(r => r.ProductId == productId && r.IsApproved, tracked: false);

        var product = await _unitOfWork.Products.GetByIdAsync(productId);
        if (product == null) return;

        var list = approvedReviews.ToList();
        product.AverageRating = list.Count != 0
            ? list.Average(r => r.Rating)
            : 0;

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveAsync();
    }

    [HttpGet]
    public async Task<IActionResult> ExportWord()
    {
        var reviews = await _unitOfWork.Reviews.GetAllAsync("User,Product", tracked: false);

        var sb = new System.Text.StringBuilder();
        sb.Append("<html xmlns:o='urn:schemas-microsoft-com:office:office' xmlns:w='urn:schemas-microsoft-com:office:word' xmlns='http://www.w3.org/TR/REC-html40'>");
        sb.Append("<head><meta charset='utf-8'><title>Reviews Export</title>");
        sb.Append("<style>");
        sb.Append("body { font-family: 'Inter', sans-serif; color: #191c1e; }");
        sb.Append("h1 { color: #4648d4; text-align: center; font-family: 'Inter', sans-serif; margin-bottom: 20px; }");
        sb.Append("table { width: 100%; border-collapse: collapse; margin-top: 20px; border: 1px solid #e0e3e5; }");
        sb.Append("th { background-color: #f2f4f6; color: #464554; padding: 12px; text-align: left; border: 1px solid #e0e3e5; font-size: 14px; text-transform: uppercase; }");
        sb.Append("td { padding: 12px; border: 1px solid #e0e3e5; color: #191c1e; font-size: 14px; vertical-align: top; }");
        sb.Append(".approved { color: #059669; font-weight: bold; }");
        sb.Append(".rejected { color: #e11d48; font-weight: bold; }");
        sb.Append(".pending { color: #d97706; font-weight: bold; }");
        sb.Append("</style>");
        sb.Append("</head><body>");
        sb.Append("<h1>Product Reviews Report</h1>");
        sb.Append("<table>");
        sb.Append("<tr><th>Product</th><th>Customer</th><th>Rating</th><th>Comment</th><th>Status</th></tr>");

        foreach (var r in reviews)
        {
            var pName = System.Net.WebUtility.HtmlEncode(r.Product?.Name ?? "Unknown Product");
            var uName = System.Net.WebUtility.HtmlEncode(r.User?.FullName ?? "Unknown User");

            string statusLabel = r.IsApproved
                ? "<span class='approved'>Approved</span>"
                : r.IsRejected
                    ? "<span class='rejected'>Rejected</span>"
                    : "<span class='pending'>Pending</span>";

            string commentEscaped = System.Net.WebUtility.HtmlEncode(r.Comment ?? string.Empty);

            sb.Append($"<tr><td>{pName}</td><td>{uName}</td><td>{r.Rating} / 5</td><td>{commentEscaped}</td><td>{statusLabel}</td></tr>");
        }

        sb.Append("</table></body></html>");

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "application/msword", "Reviews_Export.doc");
    }
}
