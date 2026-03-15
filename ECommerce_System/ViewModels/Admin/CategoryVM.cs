using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace ECommerce_System.ViewModels.Admin;

/// <summary>Used in Index and Details views — display only.</summary>
public class CategoryVM
{
    public int    Id                  { get; set; }
    public string Name                { get; set; } = string.Empty;
    public string Slug                { get; set; } = string.Empty;
    public string? ParentCategoryName { get; set; }
    public int    SubCategoriesCount  { get; set; }
    public int    ProductsCount       { get; set; }
}

/// <summary>Shared between Create and Edit forms.</summary>
public class CategoryFormVM
{
    public int Id { get; set; }  // 0 for Create

    [Required(ErrorMessage = "Name is required")]
    [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    [Display(Name = "Category Name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug is required")]
    [MaxLength(120, ErrorMessage = "Slug cannot exceed 120 characters")]
    [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$",
        ErrorMessage = "Slug must be lowercase letters, numbers and hyphens only")]
    public string Slug { get; set; } = string.Empty;

    [Display(Name = "Parent Category")]
    public int? ParentCategoryId { get; set; }

    /// <summary>Dropdown options — populated by Controller.</summary>
    public IEnumerable<SelectListItem> ParentCategories { get; set; } = [];
}

/// <summary>Used in Delete confirmation view.</summary>
public class CategoryDeleteVM
{
    public int    Id                  { get; set; }
    public string Name                { get; set; } = string.Empty;
    public string? ParentCategoryName { get; set; }
    public int    SubCategoriesCount  { get; set; }
    public int    ProductsCount       { get; set; }
}
