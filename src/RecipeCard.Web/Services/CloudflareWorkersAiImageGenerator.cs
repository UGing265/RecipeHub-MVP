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

        var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("Cloudflare AI không trả về dữ liệu ảnh nào.");
        }

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
}
