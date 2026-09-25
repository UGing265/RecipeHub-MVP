using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class CloudflareWorkersAiPromptTranslator : IAiPromptTranslator
{
    private readonly HttpClient _httpClient;
    private readonly CloudflareOptions _options;

    public CloudflareWorkersAiPromptTranslator(
        HttpClient httpClient,
        IOptions<CloudflareOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<string> TranslateVietnameseToEnglishAsync(
        string vietnamesePrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vietnamesePrompt))
        {
            throw new ArgumentException("Prompt không được để trống.", nameof(vietnamesePrompt));
        }

        throw new NotImplementedException();
    }
}
