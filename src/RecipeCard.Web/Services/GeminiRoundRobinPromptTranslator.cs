using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class GeminiRoundRobinPromptTranslator : IAiPromptTranslator
{
    private const string GeminiModel = "gemini-2.5-flash";
    private const string GeminiEndpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent";
    private const int MaxRounds = 5;

    private const string SystemInstructionText =
        "You are a beverage preparation visual prompt engineer for FLUX image generator. " +
        "Convert the Vietnamese beverage recipe step into a clear, realistic English visual description of the action. " +
        "Translate Vietnamese F&B terms accurately: 'đường nước' or 'nước đường' to 'sugar syrup', 'đá' or 'đá viên' to 'ice cubes', " +
        "'ly giấy' to 'paper cup', 'ly thủy tinh' to 'clear glass cup'. " +
        "Do not output step numbers, prefixes, quotes, markdown, or conversational filler. " +
        "Output only the English visual action description.";

    private readonly HttpClient _httpClient;
    private readonly CloudflareWorkersAiPromptTranslator _cloudflareFallbackTranslator;
    private readonly GeminiKeyCursor _keyCursor;
    private readonly GeminiOptions _options;

    public GeminiRoundRobinPromptTranslator(
        HttpClient httpClient,
        CloudflareWorkersAiPromptTranslator cloudflareFallbackTranslator,
        GeminiKeyCursor keyCursor,
        IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cloudflareFallbackTranslator = cloudflareFallbackTranslator ?? throw new ArgumentNullException(nameof(cloudflareFallbackTranslator));
        _keyCursor = keyCursor ?? throw new ArgumentNullException(nameof(keyCursor));
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
                maxOutputTokens = 150
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
                throw new InvalidOperationException("Yêu cầu đến dịch vụ Gemini AI bị quá thời gian chờ (timeout).", ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Không thể kết nối đến dịch vụ Gemini AI.", ex);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests) // 429
                {
                    var isLastAttempt = (attempt == totalAttempts - 1);
                    if (isLastAttempt)
                    {
                        return await _cloudflareFallbackTranslator.TranslateVietnameseToEnglishAsync(vietnamesePrompt, cancellationToken);
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
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
                    throw new InvalidOperationException("Dịch vụ Gemini AI không trả về kết quả dịch hợp lệ.");
                }

                var sanitized = SanitizeOutput(translatedText);
                if (string.IsNullOrWhiteSpace(sanitized))
                {
                    throw new InvalidOperationException("Dịch vụ Gemini AI không trả về kết quả dịch hợp lệ.");
                }

                return sanitized;
            }
        }

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
