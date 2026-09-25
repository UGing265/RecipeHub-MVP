using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    public async Task<string> TranslateVietnameseToEnglishAsync(
        string vietnamesePrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vietnamesePrompt))
        {
            throw new ArgumentException("Prompt không được để trống.", nameof(vietnamesePrompt));
        }

        _options.Validate();

        var model = !string.IsNullOrWhiteSpace(_options.TranslationModel)
            ? _options.TranslationModel
            : "@cf/meta/m2m100-1.2b";

        var endpoint = $"https://api.cloudflare.com/client/v4/accounts/{_options.AccountId}/ai/run/{model}";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        var payload = JsonSerializer.Serialize(new
        {
            text = vietnamesePrompt,
            source_lang = "vi",
            target_lang = "en"
        });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Không thể kết nối đến dịch vụ Cloudflare Workers AI.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            string userFriendlyError = "Dịch vụ AI không thể xử lý yêu cầu lúc này.";

            try
            {
                using var doc = JsonDocument.Parse(errorBody);
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
                {
                    var msg = errors[0].GetProperty("message").GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        userFriendlyError = $"Lỗi từ Cloudflare AI: {msg}";
                    }
                }
            }
            catch
            {
                // Fallback to generic message to avoid leaking any raw response structure
            }

            throw new InvalidOperationException(userFriendlyError);
        }

        string responseBody;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Không thể đọc phản hồi từ dịch vụ Cloudflare AI.", ex);
        }

        string? translatedText = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("result", out var resultElement))
            {
                if (resultElement.ValueKind == JsonValueKind.Object &&
                    resultElement.TryGetProperty("translated_text", out var textProp) &&
                    textProp.ValueKind == JsonValueKind.String)
                {
                    translatedText = textProp.GetString();
                }
                else if (resultElement.ValueKind == JsonValueKind.Array &&
                         resultElement.GetArrayLength() > 0)
                {
                    var firstItem = resultElement[0];
                    if (firstItem.ValueKind == JsonValueKind.Object &&
                        firstItem.TryGetProperty("translated_text", out var arrayTextProp) &&
                        arrayTextProp.ValueKind == JsonValueKind.String)
                    {
                        translatedText = arrayTextProp.GetString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Dữ liệu phản hồi từ dịch vụ Cloudflare AI không hợp lệ.", ex);
        }

        if (string.IsNullOrWhiteSpace(translatedText))
        {
            throw new InvalidOperationException("Dịch vụ Cloudflare AI không trả về kết quả dịch hợp lệ.");
        }

        return translatedText.Trim();
    }
}
