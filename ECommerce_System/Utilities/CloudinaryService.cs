using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace ECommerce_System.Utilities;

/// <summary>
/// Wraps CloudinaryDotNet for upload/delete operations.
/// Falls back to private local storage when Cloudinary credentials are not configured.
/// </summary>
public interface ICloudinaryService
{
    Task<(string Url, string PublicId)> UploadAsync(IFormFile file, string folder);
    Task DeleteAsync(string publicId);
}

public class CloudinaryService : ICloudinaryService
{
    private readonly CloudinarySettings _settings;
    private readonly IWebHostEnvironment _env;
    private readonly Cloudinary? _cloudinary;

    public CloudinaryService(IOptions<CloudinarySettings> settings, IWebHostEnvironment env)
    {
        _settings = settings.Value;
        _env = env;

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

        return await SaveLocallyAsync(file, folder);
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
        };

        var result = await _cloudinary!.UploadAsync(uploadParams);

        if (result.Error is not null)
            throw new InvalidOperationException($"Cloudinary upload failed: {result.Error.Message}");

        return (result.SecureUrl.ToString(), result.PublicId);
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
