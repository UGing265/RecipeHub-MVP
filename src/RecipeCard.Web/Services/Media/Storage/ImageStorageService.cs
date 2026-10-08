using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace RecipeCard.Web.Services;

public class ImageStorageService : IImageStorageService
{
    private readonly string _uploadFolder;
    private readonly IImageValidator _validator;

    public ImageStorageService(IWebHostEnvironment environment, IImageValidator? validator = null)
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }
        _uploadFolder = Path.Combine(webRoot, "uploads", "steps");
        _validator = validator ?? new ImageValidator();
    }

    public async Task<StoredImage> UploadAsync(
        Stream image,
        ValidatedImageInfo info,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);

        Directory.CreateDirectory(_uploadFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}{info.Extension}";
        var destinationPath = Path.Combine(_uploadFolder, uniqueFileName);

        if (image.CanSeek)
        {
            image.Position = 0;
        }

        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await image.CopyToAsync(fileStream, cancellationToken);

        return new StoredImage(
            ProviderPublicId: uniqueFileName,
            DeliveryUrl: $"/uploads/steps/{uniqueFileName}",
            ByteSize: info.ByteSize,
            MimeType: info.MimeType,
            OriginalFileName: info.OriginalFileName
        );
    }

    public Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default)
    {
        DeleteFile(providerPublicId);
        return Task.CompletedTask;
    }

    public async Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var info = _validator.Validate(file);

        Directory.CreateDirectory(_uploadFolder);

        var uniqueFileName = $"{Guid.NewGuid():N}{info.Extension}";
        var destinationPath = Path.Combine(_uploadFolder, uniqueFileName);

        using var stream = file.OpenReadStream();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);

        return uniqueFileName;
    }

    public void DeleteFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        // Prevent directory traversal attacks
        var cleanFileName = Path.GetFileName(fileName);
        if (cleanFileName != fileName)
        {
            return;
        }

        var fullPath = Path.Combine(_uploadFolder, cleanFileName);
        if (File.Exists(fullPath))
        {
            try
            {
                File.Delete(fullPath);
            }
            catch
            {
                // Ignored to avoid crashing background operations
            }
        }
    }
}
