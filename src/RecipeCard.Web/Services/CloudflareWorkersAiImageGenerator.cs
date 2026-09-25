using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class CloudflareWorkersAiImageGenerator : IAiImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly CloudflareOptions _options;
    private readonly IImageValidator _validator;

    public CloudflareWorkersAiImageGenerator(
        HttpClient httpClient,
        IOptions<CloudflareOptions> options,
        IImageValidator validator)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
    }

    public async Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Mô tả tạo ảnh (prompt) không được để trống.", nameof(prompt));
        }

        _options.Validate();

        var model = !string.IsNullOrWhiteSpace(_options.Model)
            ? _options.Model
            : "@cf/black-forest-labs/flux-1-schnell";

        var endpoint = $"https://api.cloudflare.com/client/v4/accounts/{_options.AccountId}/ai/run/{model}";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        var payload = JsonSerializer.Serialize(new { prompt });
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

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var rawBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (rawBytes.Length == 0)
        {
            throw new InvalidOperationException("Cloudflare AI không trả về dữ liệu ảnh nào.");
        }

        var imageBytes = ExtractImageBytes(rawBytes, contentType);

        // Validate image bytes and extract mime type
        ValidatedImageInfo info;
        try
        {
            info = _validator.ValidateBytes(imageBytes);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Dữ liệu ảnh trả về từ AI không đúng định dạng hình ảnh hợp lệ.", ex);
        }

        return new GeneratedImage(
            ImageBytes: imageBytes,
            MimeType: info.MimeType,
            Model: model
        );
    }

    private static byte[] ExtractImageBytes(byte[] rawBytes, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return rawBytes;
        }

        bool isJsonContentType = !string.IsNullOrWhiteSpace(contentType) &&
            (contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
             contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

        int firstNonWhitespace = -1;
        for (int i = 0; i < rawBytes.Length; i++)
        {
            if (!char.IsWhiteSpace((char)rawBytes[i]))
            {
                firstNonWhitespace = i;
                break;
            }
        }

        bool looksLikeJson = firstNonWhitespace >= 0 && rawBytes[firstNonWhitespace] == (byte)'{';

        if (isJsonContentType || looksLikeJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBytes);
                string? base64String = null;

                if (doc.RootElement.TryGetProperty("result", out var resultEl))
                {
                    if (resultEl.ValueKind == JsonValueKind.Object && resultEl.TryGetProperty("image", out var imageEl))
                    {
                        base64String = imageEl.GetString();
                    }
                    else if (resultEl.ValueKind == JsonValueKind.String)
                    {
                        base64String = resultEl.GetString();
                    }
                }

                if (string.IsNullOrWhiteSpace(base64String) && doc.RootElement.TryGetProperty("image", out var directImageEl))
                {
                    base64String = directImageEl.GetString();
                }

                if (!string.IsNullOrWhiteSpace(base64String))
                {
                    var commaIdx = base64String.IndexOf(',');
                    if (commaIdx >= 0 && base64String.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        base64String = base64String[(commaIdx + 1)..];
                    }

                    return Convert.FromBase64String(base64String.Trim());
                }

                if (doc.RootElement.TryGetProperty("errors", out var errorsEl) && errorsEl.GetArrayLength() > 0)
                {
                    var errorMsg = errorsEl[0].GetProperty("message").GetString();
                    if (!string.IsNullOrWhiteSpace(errorMsg))
                    {
                        throw new InvalidOperationException($"Lỗi từ Cloudflare AI: {errorMsg}");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Fall back to treating rawBytes as binary image data
            }
        }

        return rawBytes;
    }
}
