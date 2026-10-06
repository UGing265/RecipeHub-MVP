using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class CloudflareWorkersAiImageGenerator : IAiImageGenerator
{
    private const string DefaultModel = "@cf/bytedance/stable-diffusion-xl-lightning";
    private const string NegativePromptText = "text, watermark, labels, letters, deformed hands, extra fingers, poor quality, bad anatomy, cartoon, 3d render";

    private readonly HttpClient _httpClient;
    private readonly CloudflareOptions _options;
    private readonly IImageValidator _validator;
    private readonly ILogger<CloudflareWorkersAiImageGenerator>? _logger;

    public CloudflareWorkersAiImageGenerator(
        HttpClient httpClient,
        IOptions<CloudflareOptions> options,
        IImageValidator validator,
        ILogger<CloudflareWorkersAiImageGenerator>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new AiImageGenerationRequest(prompt, Models.AspectRatioPreset.Square1x1, 1024, 1024);
        return GenerateAsync(request, cancellationToken);
    }

    public async Task<GeneratedImage> GenerateAsync(AiImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Mô tả tạo ảnh (prompt) không được để trống.", nameof(request));
        }

        _options.Validate();

        var model = !string.IsNullOrWhiteSpace(_options.Model)
            ? _options.Model
            : DefaultModel;

        var endpoint = $"https://api.cloudflare.com/client/v4/accounts/{_options.AccountId}/ai/run/{model}";

        var (width, height) = request.Preset.ToDimensions();
        if (request.Width > 0 && request.Height > 0)
        {
            width = request.Width;
            height = request.Height;
        }

        _logger?.LogInformation("[TẠO ẢNH AI] Đang gửi yêu cầu sinh ảnh sang Cloudflare SDXL-Lightning (Preset: {Preset}, {Width}x{Height})",
            request.Preset.ToDisplayName(), width, height);

        var stopwatch = Stopwatch.StartNew();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

        // SDXL-Lightning accepts JSON payload with prompt, width, height, num_steps, negative_prompt
        var payloadObj = new Dictionary<string, object>
        {
            ["prompt"] = request.Prompt,
            ["negative_prompt"] = NegativePromptText,
            ["width"] = width,
            ["height"] = height,
            ["num_steps"] = 8
        };
        if (request.Seed.HasValue)
        {
            payloadObj["seed"] = request.Seed.Value;
        }
        var jsonPayload = JsonSerializer.Serialize(payloadObj);
        httpRequest.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogError("[TẠO ẢNH AI] -> Quá thời gian chờ phản hồi từ Cloudflare AI (Timeout).");
            throw new InvalidOperationException("Thời gian tạo ảnh từ Cloudflare AI quá lâu (quá 120 giây). Vui lòng thử lại lúc server AI rảnh hơn.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError("[TẠO ẢNH AI] -> Không thể kết nối đến Cloudflare AI: {Message}", ex.Message);
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
                // Fallback generic
            }

            _logger?.LogError("[TẠO ẢNH AI] -> Tạo ảnh THẤT BẠI ({ElapsedMs}ms): {Error}",
                stopwatch.ElapsedMilliseconds, userFriendlyError);
            throw new InvalidOperationException(userFriendlyError);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var rawBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (rawBytes.Length == 0)
        {
            _logger?.LogError("[TẠO ẢNH AI] -> Thất bại: Cloudflare không trả về dữ liệu ảnh.");
            throw new InvalidOperationException("Cloudflare AI không trả về dữ liệu ảnh nào.");
        }

        var imageBytes = ExtractImageBytes(rawBytes, contentType);

        ValidatedImageInfo info;
        try
        {
            info = _validator.ValidateBytes(imageBytes);
        }
        catch (Exception ex)
        {
            _logger?.LogError("[TẠO ẢNH AI] -> Dữ liệu ảnh trả về không hợp lệ: {Message}", ex.Message);
            throw new InvalidOperationException("Dữ liệu ảnh trả về từ AI không đúng định dạng hình ảnh hợp lệ.", ex);
        }

        _logger?.LogInformation("[TẠO ẢNH AI] -> Đã tạo ảnh THÀNH CÔNG ({ElapsedMs}ms, {KiloBytes} KB).",
            stopwatch.ElapsedMilliseconds, imageBytes.Length / 1024);

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
                // Fall back
            }
        }

        return rawBytes;
    }
}
