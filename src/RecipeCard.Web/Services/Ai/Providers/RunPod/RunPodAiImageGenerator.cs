using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class RunPodAiImageGenerator : IAiImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly RunPodOptions _options;
    private readonly IImageValidator _validator;
    private readonly ILogger<RunPodAiImageGenerator> _logger;

    public RunPodAiImageGenerator(
        HttpClient httpClient,
        IOptions<RunPodOptions> options,
        IImageValidator validator,
        ILogger<RunPodAiImageGenerator> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _validator = validator;
        _logger = logger;
    }

    public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return GenerateAsync(new AiImageGenerationRequest(prompt, RecipeCard.Web.Models.AspectRatioPreset.Square1x1, 1024, 1024), cancellationToken);
    }

    public async Task<GeneratedImage> GenerateAsync(AiImageGenerationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.EndpointId))
        {
            throw new InvalidOperationException("RunPod:EndpointId chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("RunPod:ApiKey chưa được cấu hình.");
        }

        long seed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        string size = $"{request.Width}x{request.Height}";

        var payload = new RunPodJobRequest
        {
            Input = new RunPodJobInput
            {
                Prompt = request.Prompt,
                Size = size,
                Seed = seed
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var runRequest = new HttpRequestMessage(HttpMethod.Post, $"https://api.runpod.ai/v2/{_options.EndpointId}/run")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        runRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        HttpResponseMessage runResponse;
        try
        {
            runResponse = await _httpClient.SendAsync(runRequest, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kết nối khi gửi job sang RunPod");
            throw new InvalidOperationException("Không thể kết nối tới dịch vụ RunPod.", ex);
        }

        using (runResponse)
        {
            if (!runResponse.IsSuccessStatusCode)
            {
                _logger.LogError("RunPod trả về lỗi HTTP {StatusCode}", (int)runResponse.StatusCode);
                throw new InvalidOperationException($"RunPod trả về mã lỗi HTTP {(int)runResponse.StatusCode}.");
            }

            var runBody = await runResponse.Content.ReadAsStringAsync(cancellationToken);
            var envelope = JsonSerializer.Deserialize<RunPodJobEnvelope>(runBody);
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.Id))
            {
                throw new InvalidOperationException("Phản hồi từ RunPod không hợp lệ hoặc thiếu Job ID.");
            }

            var jobId = envelope.Id;
            var timeout = TimeSpan.FromSeconds(Math.Max(10, _options.RequestTimeoutSeconds));
            var pollInterval = TimeSpan.FromMilliseconds(Math.Max(500, _options.PollIntervalMilliseconds));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(pollInterval, cts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.runpod.ai/v2/{_options.EndpointId}/status/{jobId}");
                statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

                using var statusResponse = await _httpClient.SendAsync(statusRequest, cts.Token);
                if (!statusResponse.IsSuccessStatusCode)
                {
                    continue;
                }

                var statusBody = await statusResponse.Content.ReadAsStringAsync(cts.Token);
                var statusEnvelope = JsonSerializer.Deserialize<RunPodJobEnvelope>(statusBody);
                if (statusEnvelope == null)
                {
                    continue;
                }
                var status = statusEnvelope.Status?.ToUpperInvariant();
                _logger.LogInformation("[RUNPOD] Job {JobId} -> Trạng thái: {Status}", jobId, status);
                if (status == "COMPLETED")
                {
                    var imageBytes = await ExtractImageBytesAsync(statusEnvelope.Output, cts.Token);
                    var validation = _validator.ValidateBytes(imageBytes);
                    return new GeneratedImage(imageBytes, validation.MimeType, "runpod/flux-1-dev");
                }

                if (status is "FAILED" or "CANCELLED" or "TIMED_OUT")
                {
                    _logger.LogError("Job RunPod {JobId} kết thúc với trạng thái {Status}", jobId, status);
                    throw new InvalidOperationException($"Quá trình sinh ảnh trên RunPod thất bại (Trạng thái: {status}).");
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            throw new TimeoutException($"Quá thời gian chờ phản hồi từ RunPod ({timeout.TotalSeconds} giây).");
        }
    }

    private async Task<byte[]> ExtractImageBytesAsync(JsonElement? output, CancellationToken cancellationToken)
    {
        if (output == null || output.Value.ValueKind == JsonValueKind.Null || output.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("RunPod hoàn thành nhưng không trả về dữ liệu ảnh (output trống).");
        }

        var elem = output.Value;

        // Output là string (Base64 hoặc URL)
        if (elem.ValueKind == JsonValueKind.String)
        {
            var str = elem.GetString();
            return await ParseStringOutputAsync(str, cancellationToken);
        }

        // Output là array (chứa URL hoặc Base64 ở phần tử đầu tiên)
        if (elem.ValueKind == JsonValueKind.Array && elem.GetArrayLength() > 0)
        {
            var first = elem[0];
            if (first.ValueKind == JsonValueKind.String)
            {
                return await ParseStringOutputAsync(first.GetString(), cancellationToken);
            }
            if (first.ValueKind == JsonValueKind.Object)
            {
                return await ExtractFromObjectAsync(first, cancellationToken);
            }
        }

        // Output là object (chứa image, images, url, etc.)
        if (elem.ValueKind == JsonValueKind.Object)
        {
            return await ExtractFromObjectAsync(elem, cancellationToken);
        }

        throw new InvalidOperationException("Định dạng dữ liệu trả về từ RunPod không được hỗ trợ.");
    }

    private async Task<byte[]> ExtractFromObjectAsync(JsonElement obj, CancellationToken cancellationToken)
    {
        // Schema thực tế của endpoint RunPod FLUX (chuẩn OpenAI DALL-E/FLUX worker):
        // Các trường: "b64_json", "url", "revised_prompt"
        if (obj.TryGetProperty("b64_json", out var b64Prop) && b64Prop.ValueKind == JsonValueKind.String)
        {
            var b64Str = b64Prop.GetString();
            if (!string.IsNullOrWhiteSpace(b64Str))
            {
                return await ParseStringOutputAsync(b64Str, cancellationToken);
            }
        }

        if (obj.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
        {
            var urlStr = urlProp.GetString();
            if (!string.IsNullOrWhiteSpace(urlStr))
            {
                return await ParseStringOutputAsync(urlStr, cancellationToken);
            }
        }

        // Fallback kiểm tra các thuộc tính ảnh thông dụng khác nếu worker trả về định dạng khác
        string[] candidateProps = ["image", "images", "image_url", "output", "data", "result", "file", "img"];
        foreach (var prop in candidateProps)
        {
            if (obj.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.String)
                {
                    return await ParseStringOutputAsync(val.GetString(), cancellationToken);
                }
                if (val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0)
                {
                    var firstItem = val[0];
                    if (firstItem.ValueKind == JsonValueKind.String)
                    {
                        return await ParseStringOutputAsync(firstItem.GetString(), cancellationToken);
                    }
                    if (firstItem.ValueKind == JsonValueKind.Object)
                    {
                        return await ExtractFromObjectAsync(firstItem, cancellationToken);
                    }
                }
                if (val.ValueKind == JsonValueKind.Object)
                {
                    return await ExtractFromObjectAsync(val, cancellationToken);
                }
            }
        }

        var keys = string.Join(", ", obj.EnumerateObject().Select(p => p.Name));
        _logger.LogError("RunPod trả về object không chứa trường ảnh hợp lệ. Keys: [{Keys}]", keys);
        throw new InvalidOperationException("Không thể trích xuất dữ liệu ảnh từ phản hồi của RunPod.");
    }

    private async Task<byte[]> ParseStringOutputAsync(string? value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Dữ liệu ảnh từ RunPod rỗng.");
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Chỉ chấp nhận URL ảnh bảo mật HTTPS từ RunPod.");
            }

            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Địa chỉ máy chủ tải ảnh không an toàn.");
            }

            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Không thể phân giải tên miền ảnh: {uri.DnsSafeHost}", ex);
            }

            if (addresses.Length == 0 || addresses.Any(IsUnsafeIp))
            {
                throw new InvalidOperationException("Địa chỉ IP tải ảnh không an toàn (Private/Loopback).");
            }
            using var imgResponse = await _httpClient.GetAsync(uri, cancellationToken);
            if ((int)imgResponse.StatusCode is >= 300 and < 400)
            {
                throw new InvalidOperationException("Không cho phép chuyển hướng URL ảnh từ RunPod (Redirect disallowed).");
            }
            if (!imgResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Không thể tải ảnh từ URL do RunPod trả về (HTTP {(int)imgResponse.StatusCode}).");
            }
            return await imgResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var b64 = value;
        var commaIdx = b64.IndexOf(',');
        if (commaIdx >= 0 && b64.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            b64 = b64[(commaIdx + 1)..];
        }

        try
        {
            return Convert.FromBase64String(b64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Dữ liệu ảnh trả về từ RunPod không phải là Base64 hoặc URL hợp lệ.", ex);
        }
    }

    private static bool IsUnsafeIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   bytes[0] == 127 ||
                   bytes[0] == 0;
        }
        return false;
    }
}
