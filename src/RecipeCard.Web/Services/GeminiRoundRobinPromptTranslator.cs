using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class GeminiRoundRobinPromptTranslator : IAiPromptTranslator
{
    private const string GeminiModel = "gemini-2.5-flash";
    private const string GeminiEndpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent";
    private const int MaxRounds = 5;

    private const string SystemInstructionText = AiPromptTranslationConstants.FaithfulSystemInstruction;

    private readonly HttpClient _httpClient;
    private readonly CloudflareWorkersAiPromptTranslator _cloudflareFallbackTranslator;
    private readonly GeminiKeyCursor _keyCursor;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiRoundRobinPromptTranslator>? _logger;

    public GeminiRoundRobinPromptTranslator(
        HttpClient httpClient,
        CloudflareWorkersAiPromptTranslator cloudflareFallbackTranslator,
        GeminiKeyCursor keyCursor,
        IOptions<GeminiOptions> options,
        ILogger<GeminiRoundRobinPromptTranslator>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cloudflareFallbackTranslator = cloudflareFallbackTranslator ?? throw new ArgumentNullException(nameof(cloudflareFallbackTranslator));
        _keyCursor = keyCursor ?? throw new ArgumentNullException(nameof(keyCursor));
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

        _logger?.LogInformation("[DỊCH PROMPT] Đang dịch: \"{Prompt}\"", vietnamesePrompt);
        var stopwatch = Stopwatch.StartNew();

        var keys = _options.ApiKeys;
        var n = keys.Count;
        var totalAttempts = MaxRounds * n;
        var startIndex = _keyCursor.NextStart(n);

        var payload = JsonSerializer.Serialize(new
        {
            systemInstruction = new
            {
                parts = new object[]
                {
                    new { text = SystemInstructionText }
                }
            },
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = vietnamesePrompt }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = AiPromptTranslationConstants.MaxOutputTokens
            }
        });

        for (var attempt = 0; attempt < totalAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var keyIndex = (startIndex + attempt) % n;
            var currentApiKey = keys[keyIndex];

            using var request = new HttpRequestMessage(HttpMethod.Post, GeminiEndpoint);
            request.Headers.Add("x-goog-api-key", currentApiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError("[DỊCH PROMPT] -> Quá thời gian chờ (timeout) sau {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException("Yêu cầu đến dịch vụ Gemini AI bị quá thời gian chờ (timeout).", ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError("[DỊCH PROMPT] -> Không thể kết nối đến Gemini: {Message}", ex.Message);
                throw new InvalidOperationException("Không thể kết nối đến dịch vụ Gemini AI.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests) // 429
                {
                    _logger?.LogWarning("[DỊCH PROMPT] Key #{KeyNumber} bị 429 (giới hạn lượt gọi), đổi key khác...", keyIndex + 1);
                    var isLastAttempt = (attempt == totalAttempts - 1);
                    if (isLastAttempt)
                    {
                        _logger?.LogWarning("[DỊCH PROMPT] Tất cả key Gemini đều bận, chuyển sang Cloudflare fallback...");
                        return await _cloudflareFallbackTranslator.TranslateVietnameseToEnglishAsync(vietnamesePrompt, cancellationToken);
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogError("[DỊCH PROMPT] -> Lỗi HTTP {StatusCode} từ Gemini.", (int)response.StatusCode);
                    throw new InvalidOperationException("Dịch vụ Gemini AI không thể xử lý yêu cầu lúc này.");
                }

                string responseBody;
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException("Yêu cầu đọc phản hồi từ dịch vụ Gemini AI bị quá thời gian chờ (timeout).", ex);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Không thể đọc phản hồi từ dịch vụ Gemini AI.", ex);
                }

                var translatedText = ParseGeminiResponse(responseBody);
                if (string.IsNullOrWhiteSpace(translatedText))
                {
                    _logger?.LogError("[DỊCH PROMPT] -> Phản hồi Gemini không có nội dung dịch.");
                    throw new InvalidOperationException("Dịch vụ Gemini AI không trả về kết quả dịch hợp lệ.");
                }

                var sanitized = SanitizeOutput(translatedText);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    _logger?.LogError("[DỊCH PROMPT] -> Kết quả dịch rỗng sau khi làm sạch.");
                    throw new InvalidOperationException("Dịch vụ Gemini AI không trả về kết quả dịch hợp lệ.");
                }

                _logger?.LogInformation("[DỊCH PROMPT] -> Đã dịch xong ({ElapsedMs}ms): \"{Result}\"",
                    stopwatch.ElapsedMilliseconds, sanitized);
                return sanitized;
            }
        }

        _logger?.LogWarning("[DỊCH PROMPT] Hết lượt thử Gemini, chuyển sang Cloudflare fallback...");
        return await _cloudflareFallbackTranslator.TranslateVietnameseToEnglishAsync(vietnamesePrompt, cancellationToken);
    }

    private static string? ParseGeminiResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return null;
            }

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                // Ignore thought parts if any, or non-text parts
                if (part.TryGetProperty("thought", out var isThought) && isThought.GetBoolean())
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                {
                    var t = textProp.GetString();
                    if (!string.IsNullOrEmpty(t))
                    {
                        sb.Append(t);
                    }
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch
        {
            return null;
        }
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
