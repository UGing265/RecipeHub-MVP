using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public record AiImageGenerationRequest(
    string Prompt,
    AspectRatioPreset Preset = AspectRatioPreset.Square1x1,
    int Width = 1024,
    int Height = 1024,
    long? Seed = null);
