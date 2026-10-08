using Microsoft.AspNetCore.Http;

namespace RecipeCard.Web.Services;

public record StoredImage(
    string ProviderPublicId,
    string DeliveryUrl,
    long ByteSize,
    string MimeType,
    string? OriginalFileName = null
);

public interface IImageStorageService
{
    Task<StoredImage> UploadAsync(
        Stream image, ValidatedImageInfo info, CancellationToken cancellationToken = default);

    Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default);

    // Transitional helper methods for legacy single-step file storage
    Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    void DeleteFile(string? fileName) { }
}
