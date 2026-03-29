namespace ECommerce_System.Utilities;

public class CloudinarySettings
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey    { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(CloudName) &&
        !string.IsNullOrWhiteSpace(ApiKey)    &&
        !string.IsNullOrWhiteSpace(ApiSecret);
}
