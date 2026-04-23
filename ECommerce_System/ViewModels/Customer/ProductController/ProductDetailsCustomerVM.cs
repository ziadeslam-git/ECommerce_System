using ECommerce_System.Models;

namespace ECommerce_System.ViewModels.Customer.ProductController
{
   
    
        public class ProductDetailsVM
        {
            public Product Product { get; set; }
            public List<Review> Reviews { get; set; }
            public List<Product> RelatedProducts { get; set; }
        }
    }

