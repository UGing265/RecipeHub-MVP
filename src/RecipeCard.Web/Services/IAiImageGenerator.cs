namespace RecipeCard.Web.Services;

public record GeneratedImage(
    byte[] ImageBytes,
    string MimeType,
    string Model
);

public interface IAiImageGenerator
{
    Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}
