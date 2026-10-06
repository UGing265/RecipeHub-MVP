namespace RecipeCard.Web.Models;

public sealed class Recipe
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GeneralNote { get; set; }
    public int? FinalMediaAssetId { get; set; }
    public MediaAsset? FinalMediaAsset { get; set; }
    public AspectRatioPreset FinalImageAspectRatioPreset { get; set; } = AspectRatioPreset.WideLandscape16x9;

    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public List<RecipeStep> Steps { get; set; } = [];
}
