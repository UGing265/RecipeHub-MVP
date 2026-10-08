using Microsoft.Extensions.Options;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public interface IAiImageGeneratorRouter
{
    Task<GeneratedImage> GenerateAsync(AiImageGenerationRequest request, AiImageProvider? requestedProvider = null, CancellationToken cancellationToken = default);
    AiImageOptions Options { get; }
    bool IsProviderAvailable(AiImageProvider provider);
}

public class AiImageGeneratorRouter : IAiImageGeneratorRouter
{
    private readonly IAiImageGenerator _cloudflareGenerator;
    private readonly RunPodAiImageGenerator? _runpodGenerator;
    private readonly AiImageOptions _options;

    public AiImageGeneratorRouter(
        IAiImageGenerator cloudflareGenerator,
        IOptions<AiImageOptions>? options = null)
        : this(cloudflareGenerator, null, options)
    {
    }

    public AiImageGeneratorRouter(
        IAiImageGenerator cloudflareGenerator,
        RunPodAiImageGenerator? runpodGenerator,
        IOptions<AiImageOptions>? options = null)
    {
        _cloudflareGenerator = cloudflareGenerator;
        _runpodGenerator = runpodGenerator;
        _options = options?.Value ?? new AiImageOptions();
    }

    public AiImageOptions Options => _options;
    public bool IsProviderAvailable(AiImageProvider provider)
    {
        if (provider == AiImageProvider.CloudflareSchnell)
        {
            return true;
        }

        if (provider == AiImageProvider.RunPodFluxDev)
        {
            return _options.RunPodEnabled && _runpodGenerator != null;
        }

        return false;
    }


    public async Task<GeneratedImage> GenerateAsync(
        AiImageGenerationRequest request,
        AiImageProvider? requestedProvider = null,
        CancellationToken cancellationToken = default)
    {
        var provider = requestedProvider ?? _options.DefaultProvider;

        switch (provider)
        {
            case AiImageProvider.CloudflareSchnell:
                return await _cloudflareGenerator.GenerateAsync(request, cancellationToken);

            case AiImageProvider.RunPodFluxDev:
                if (!_options.RunPodEnabled)
                {
                    throw new InvalidOperationException("Model Runpod FLUX.1 Dev đang bị khóa (Cost Guard chưa được kích hoạt trong cấu hình).");
                }
                if (_runpodGenerator == null)
                {
                    throw new InvalidOperationException("Model Runpod FLUX.1 Dev chưa khả dụng vì adapter chưa được đăng ký trong hệ thống.");
                }
                return await _runpodGenerator.GenerateAsync(request, cancellationToken);

            default:
                throw new NotSupportedException($"Nhà cung cấp AI '{provider}' không được hỗ trợ.");
        }
    }
}
