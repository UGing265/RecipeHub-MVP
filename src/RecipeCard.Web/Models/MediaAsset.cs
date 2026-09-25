namespace RecipeCard.Web.Models;

public enum MediaSourceType
{
    Real = 1,
    AiIllustration = 2
}

public enum MediaAssetState
{
    Active = 1,
    DeleteFailed = 2
}

public class MediaAsset
{
    public int Id { get; set; }

    public string StorageProvider { get; set; } = "Cloudinary";

    public string ProviderPublicId { get; set; } = string.Empty;

    public string DeliveryUrl { get; set; } = string.Empty;

    public string? OriginalFileName { get; set; }

    public string MimeType { get; set; } = "image/jpeg";

    public long ByteSize { get; set; }

    public MediaSourceType SourceType { get; set; } = MediaSourceType.Real;

    public string? Caption { get; set; }

    public MediaAssetState State { get; set; } = MediaAssetState.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? AiImageDraftId { get; set; }

    public AiImageDraft? AiImageDraft { get; set; }

    public ICollection<RecipeStep> RecipeSteps { get; set; } = [];
}
