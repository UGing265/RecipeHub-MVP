using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public static class AspectRatioPresetExtensions
{
    public static (int Width, int Height) ToDimensions(this AspectRatioPreset preset) => preset switch
    {
        AspectRatioPreset.Square1x1 => (1024, 1024),
        AspectRatioPreset.StandardLandscape4x3 => (1024, 768),
        AspectRatioPreset.WideLandscape16x9 => (1280, 720),
        AspectRatioPreset.Portrait4x5 => (768, 960),
        _ => (1024, 1024)
    };

    public static string ToDisplayName(this AspectRatioPreset preset) => preset switch
    {
        AspectRatioPreset.Square1x1 => "Vuông (1:1)",
        AspectRatioPreset.StandardLandscape4x3 => "Ngang chuẩn (4:3)",
        AspectRatioPreset.WideLandscape16x9 => "Ngang rộng (16:9)",
        AspectRatioPreset.Portrait4x5 => "Dọc (4:5)",
        _ => preset.ToString()
    };
}
