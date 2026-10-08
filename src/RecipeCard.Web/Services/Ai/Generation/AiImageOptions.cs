using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public class AiImageOptions
{
    public const string SectionName = "AiImage";

    public AiImageProvider DefaultProvider { get; set; } = AiImageProvider.CloudflareSchnell;

    /// <summary>
    /// Cost guard: when false, RunPod requests are blocked server-side and the option is disabled on UI.
    /// </summary>
    public bool RunPodEnabled { get; set; } = false;
}
