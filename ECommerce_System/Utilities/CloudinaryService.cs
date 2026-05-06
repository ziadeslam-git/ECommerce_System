using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace ECommerce_System.Utilities;

/// <summary>
/// Wraps CloudinaryDotNet for upload/delete operations.
/// Requires Cloudinary credentials for public product image delivery.
/// </summary>
public interface ICloudinaryService
{
    Task<(string Url, string PublicId)> UploadAsync(IFormFile file, string folder);
    Task DeleteAsync(string publicId);
}

public class CloudinaryService : ICloudinaryService
{
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(45);

    private readonly CloudinarySettings _settings;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CloudinaryService> _logger;
    private readonly Cloudinary? _cloudinary;

    public CloudinaryService(
        IOptions<CloudinarySettings> settings,
        IWebHostEnvironment env,
        ILogger<CloudinaryService> logger)
    {
        _settings = settings.Value;
        _env = env;
        _logger = logger;

        if (_settings.IsConfigured)
        {
            var account = new Account(_settings.CloudName, _settings.ApiKey, _settings.ApiSecret);
            _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
        }
    }

    public async Task<(string Url, string PublicId)> UploadAsync(IFormFile file, string folder)
    {
        ValidateImageFile(file);

        if (_cloudinary is not null)
            return await UploadToCloudinaryAsync(file, folder);

        _logger.LogWarning(
            "Image upload blocked because Cloudinary settings are missing. CloudName={CloudNameConfigured}, ApiKey={ApiKeyConfigured}, ApiSecret={ApiSecretConfigured}",
            !string.IsNullOrWhiteSpace(_settings.CloudName),
            !string.IsNullOrWhiteSpace(_settings.ApiKey),
            !string.IsNullOrWhiteSpace(_settings.ApiSecret));

        throw new InvalidOperationException(
            "Image upload storage is not configured. Please set Cloudinary__CloudName, Cloudinary__ApiKey, and Cloudinary__ApiSecret.");
    }

    private async Task<(string Url, string PublicId)> UploadToCloudinaryAsync(IFormFile file, string folder)
    {
        await using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams
        {
            File        = new FileDescription(file.FileName, stream),
            Folder      = folder,
            UseFilename = false,
            UniqueFilename = true,
            Overwrite   = false,
            AllowedFormats = ["jpg", "jpeg", "png", "webp"],
        };

        using var cts = new CancellationTokenSource(UploadTimeout);

        ImageUploadResult result;
        try
        {
            result = await _cloudinary!.UploadAsync(uploadParams, cts.Token);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Cloudinary image upload timed out after {TimeoutSeconds}s. Folder={Folder}, FileName={FileName}, FileLength={FileLength}",
                UploadTimeout.TotalSeconds,
                folder,
                file.FileName,
                file.Length);

            throw new InvalidOperationException(
                "Image upload timed out. Please try again, or check the Cloudinary/network settings.");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex,
                "Cloudinary image upload was canceled by the HTTP client timeout. Folder={Folder}, FileName={FileName}, FileLength={FileLength}",
                folder,
                file.FileName,
                file.Length);

            throw new InvalidOperationException(
                "Image upload timed out. Please try again, or check the Cloudinary/network settings.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Cloudinary image upload failed. Folder={Folder}, FileName={FileName}, FileLength={FileLength}",
                folder,
                file.FileName,
                file.Length);

            throw new InvalidOperationException(
                "Image upload failed. Please check Cloudinary settings and try again.");
        }

        if (result.Error is not null)
            throw new InvalidOperationException($"Cloudinary upload failed: {result.Error.Message}");

        return (BuildOptimizedDeliveryUrl(result.PublicId, result.SecureUrl.ToString()), result.PublicId);
    }

    private async Task<(string Url, string PublicId)> SaveLocallyAsync(IFormFile file, string folder)
    {
        var normalizedFolder = NormalizeFolder(folder);
        var uploadsDir = GetLocalUploadsDirectory(normalizedFolder);
        Directory.CreateDirectory(uploadsDir);

        var publicId  = $"local/{Guid.NewGuid():N}";
        var fileName  = publicId.Replace("/", "_") + ".jpg";
        var fullPath  = Path.Combine(uploadsDir, fileName);

        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);

        var url = "/private-upload";
        return (url, publicId);
    }

    public async Task DeleteAsync(string publicId)
    {
        if (_cloudinary is not null && !publicId.StartsWith("local/"))
        {
            var deleteParams = new DeletionParams(publicId);
            await _cloudinary.DestroyAsync(deleteParams);
            return;
        }

        // Local file deletion
        if (publicId.StartsWith("local/"))
        {
            var fileName = publicId.Replace("local/", "local_") ;
            var uploadsRoot = Path.Combine(_env.ContentRootPath, "uploads_private");
            if (!Directory.Exists(uploadsRoot))
                return;

            var files = Directory.GetFiles(
                uploadsRoot,
                fileName + ".*",
                SearchOption.AllDirectories);
            foreach (var f in files) File.Delete(f);
        }
    }

    private static string NormalizeFolder(string folder) =>
        string.IsNullOrWhiteSpace(folder)
            ? "misc"
            : folder.Trim('/').Replace('\\', '/');

    private string GetLocalUploadsDirectory(string folder)
    {
        var segments = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(new[] { _env.ContentRootPath, "uploads_private" }.Concat(segments).ToArray());
    }

    private string BuildOptimizedDeliveryUrl(string publicId, string fallbackUrl)
    {
        if (_cloudinary is null || string.IsNullOrWhiteSpace(publicId))
            return fallbackUrl;

        return _cloudinary.Api.UrlImgUp
            .Secure(true)
            .Transform(new Transformation()
                .Width(1400)
                .Height(1400)
                .Crop("limit")
                .Quality("auto")
                .FetchFormat("auto"))
            .BuildUrl(publicId);
    }

    private static void ValidateImageFile(IFormFile file)
    {
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            throw new InvalidOperationException($"File extension '{ext}' is not allowed.");

        var allowedMimes = new[] { "image/jpeg", "image/png", "image/webp" };
        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!allowedMimes.Contains(contentType))
            throw new InvalidOperationException($"Content type '{file.ContentType}' is not allowed.");

        using var stream = file.OpenReadStream();
        var header = new byte[4];
        _ = stream.Read(header, 0, 4);
        bool isJpeg = header[0] == 0xFF && header[1] == 0xD8;
        bool isPng  = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        bool isWebP = false;
        if (file.Length >= 12)
        {
            stream.Seek(0, SeekOrigin.Begin);
            var webpHeader = new byte[12];
            _ = stream.Read(webpHeader, 0, 12);
            isWebP = webpHeader[0] == 0x52 && webpHeader[1] == 0x49 &&
                     webpHeader[2] == 0x46 && webpHeader[3] == 0x46 &&
                     webpHeader[8] == 0x57 && webpHeader[9] == 0x45 &&
                     webpHeader[10] == 0x42 && webpHeader[11] == 0x50;
        }

        if (!isJpeg && !isPng && !isWebP)
            throw new InvalidOperationException("File content does not match a valid image signature.");
    }
}
