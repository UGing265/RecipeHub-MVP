using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class CloudflareWorkersAiPromptTranslator : IAiPromptTranslator
{
    private readonly HttpClient _httpClient;
    private readonly CloudflareOptions _options;
    private readonly ILogger<CloudflareWorkersAiPromptTranslator>? _logger;

    public CloudflareWorkersAiPromptTranslator(
        HttpClient httpClient,
        IOptions<CloudflareOptions> options,
        ILogger<CloudflareWorkersAiPromptTranslator>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
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

        const string model = "@cf/meta/llama-3.1-8b-instruct";
        var endpoint = $"https://api.cloudflare.com/client/v4/accounts/{_options.AccountId}/ai/run/{model}";

        _logger?.LogInformation("[DỊCH PROMPT (Dự phòng)] Đang dịch bằng Cloudflare: \"{Prompt}\"", vietnamesePrompt);
        var stopwatch = Stopwatch.StartNew();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        var payload = JsonSerializer.Serialize(new
        {
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = AiPromptTranslationConstants.FaithfulSystemInstruction
                },
                new
                {
                    role = "user",
                    content = vietnamesePrompt
                }
            },
            max_tokens = AiPromptTranslationConstants.MaxOutputTokens
        });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError("[DỊCH PROMPT (Dự phòng)] -> Quá thời gian chờ (timeout) sau {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException("Yêu cầu đến dịch vụ Cloudflare Workers AI bị quá thời gian chờ (timeout).", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError("[DỊCH PROMPT (Dự phòng)] -> Không thể kết nối Cloudflare: {Message}", ex.Message);
            throw new InvalidOperationException("Không thể kết nối đến dịch vụ Cloudflare Workers AI.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("[DỊCH PROMPT (Dự phòng)] -> Lỗi HTTP {StatusCode}.", (int)response.StatusCode);
            throw new InvalidOperationException("Dịch vụ AI không thể xử lý yêu cầu lúc này.");
        }

        string responseBody;
        try
        {
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Yêu cầu đọc phản hồi từ dịch vụ Cloudflare AI bị quá thời gian chờ (timeout).", ex);
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
            if (doc.RootElement.TryGetProperty("result", out var resultElement) &&
                resultElement.ValueKind == JsonValueKind.Object &&
                resultElement.TryGetProperty("response", out var respProp) &&
                respProp.ValueKind == JsonValueKind.String)
            {
                translatedText = respProp.GetString();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DỊCH PROMPT (Dự phòng)] Dữ liệu JSON phản hồi không hợp lệ.");
            throw new InvalidOperationException("Dữ liệu phản hồi từ dịch vụ Cloudflare AI không hợp lệ.", ex);
        }

        if (string.IsNullOrWhiteSpace(translatedText))
        {
            _logger?.LogError("[DỊCH PROMPT (Dự phòng)] -> Cloudflare không trả về kết quả dịch hợp lệ.");
            throw new InvalidOperationException("Dịch vụ Cloudflare AI không trả về kết quả dịch hợp lệ.");
        }
        var cleaned = SanitizeOutput(translatedText);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            _logger?.LogError("[DỊCH PROMPT (Dự phòng)] -> Kết quả dịch rỗng sau khi làm sạch.");
            throw new InvalidOperationException("Dịch vụ Cloudflare AI không trả về kết quả dịch hợp lệ.");
        }

        _logger?.LogInformation("[DỊCH PROMPT (Dự phòng)] -> Đã dịch xong ({ElapsedMs}ms): \"{Result}\"",
            stopwatch.ElapsedMilliseconds, cleaned);
        return cleaned;
    }

    private static string SanitizeOutput(string text)
    {
        var clean = text.Trim();
        if (clean.StartsWith('"') && clean.EndsWith('"') && clean.Length >= 2)
        {
            clean = clean[1..^1].Trim();
        }
        if (clean.StartsWith("```") && clean.EndsWith("```") && clean.Length >= 6)
        {
            clean = clean[3..^3].Trim();
        }
        return clean;
    }
}
