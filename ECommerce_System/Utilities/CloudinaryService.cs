using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace ECommerce_System.Utilities;

/// <summary>
/// Wraps CloudinaryDotNet for upload/delete operations.
/// Falls back to local wwwroot/uploads when Cloudinary credentials are not configured.
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

        var ext       = Path.GetExtension(file.FileName);
        var publicId  = $"local/{Guid.NewGuid():N}";
        var fileName  = publicId.Replace("/", "_") + ext;
        var fullPath  = Path.Combine(uploadsDir, fileName);

        await using var fs = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(fs);

        var url = $"/uploads/{normalizedFolder}/{fileName}".Replace("\\", "/");
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
            var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads");
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
        return Path.Combine(new[] { _env.WebRootPath, "uploads" }.Concat(segments).ToArray());
    }
}
