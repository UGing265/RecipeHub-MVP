namespace RecipeCard.Web.Models;

public enum AiDraftState
{
    Generated = 1,
    Accepted = 2,
    Discarded = 3,
    Expired = 4,
    Failed = 5
}

public class AiImageDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int RecipeStepId { get; set; }

    public RecipeStep RecipeStep { get; set; } = null!;

    public string PromptSnapshot { get; set; } = string.Empty;

    public string? UserBrief { get; set; }

    public string Model { get; set; } = "@cf/black-forest-labs/flux-1-schnell";

    public string TemporaryFileName { get; set; } = string.Empty;

    public AiDraftState State { get; set; } = AiDraftState.Generated;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresUtc { get; set; }

    public MediaAsset? MediaAsset { get; set; }
}
