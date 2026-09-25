namespace RecipeCard.Web.Models;

public sealed class Ingredient
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DefaultUnit { get; set; } = string.Empty;

    public List<RecipeIngredient> RecipeIngredients { get; set; } = [];

    public static string Normalize(string? name) => (name ?? string.Empty).Trim().ToUpperInvariant();
}
