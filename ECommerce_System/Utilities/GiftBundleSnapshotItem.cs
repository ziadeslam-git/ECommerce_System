using System.Text.Json;

namespace ECommerce_System.Utilities;

public class GiftBundleSnapshotItem
{
    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? ImageUrl { get; set; }
}

public static class GiftBundleSnapshotHelper
{
    public static string Serialize(IEnumerable<GiftBundleSnapshotItem> items)
        => JsonSerializer.Serialize(items);

    public static List<GiftBundleSnapshotItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<GiftBundleSnapshotItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
