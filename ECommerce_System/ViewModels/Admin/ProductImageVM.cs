using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ECommerce_System.ViewModels.Admin
{
    public class ProductImageVM
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ImageUrl { get; set; }
        public string? PublicId { get; set; }
        public bool IsMain { get; set; }
        public int DisplayOrder { get; set; }

        public string? ProductName { get; set; }
    }

    public class UploadImageVM
    {
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Please select an image")]
        public IFormFile? ImageFile { get; set; }

        public bool IsMain { get; set; }

        public int DisplayOrder { get; set; } = 0;
    }
}
