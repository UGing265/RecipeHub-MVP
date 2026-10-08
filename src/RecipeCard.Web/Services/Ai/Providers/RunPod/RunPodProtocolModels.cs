using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecipeCard.Web.Services;

public class RunPodJobRequest
{
    [JsonPropertyName("input")]
    public RunPodJobInput Input { get; set; } = new();
}

public class RunPodJobInput
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; set; } = "1024x1024";

    [JsonPropertyName("seed")]
    public long Seed { get; set; } = 42;
}

public class RunPodJobEnvelope
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public JsonElement? Output { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
