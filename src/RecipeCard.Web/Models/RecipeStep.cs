namespace RecipeCard.Web.Models;

public sealed class RecipeStep
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public string? ImageFileName { get; set; }

    public int? MediaAssetId { get; set; }
    public MediaAsset? MediaAsset { get; set; }

    public ICollection<AiImageDraft> AiImageDrafts { get; set; } = [];
}
