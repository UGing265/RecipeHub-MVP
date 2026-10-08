namespace RecipeCard.Web.Services;

public class RunPodOptions
{
    public const string SectionName = "RunPod";

    public string EndpointId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 300;
    public int PollIntervalMilliseconds { get; set; } = 1500;
}
