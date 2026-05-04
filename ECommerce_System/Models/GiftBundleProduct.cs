namespace ECommerce_System.Models;

public class GiftBundleProduct
{
    public int Id { get; set; }
    public int GiftBundleId { get; set; }
    public int ProductId { get; set; }
    public int SortOrder { get; set; }

    public GiftBundle GiftBundle { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
