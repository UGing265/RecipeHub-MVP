using Microsoft.Extensions.Options;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiImageGeneratorRouterTests
{
    private class FakeGenerator : IAiImageGenerator
    {
        public int CallCount { get; private set; }
        public AiImageGenerationRequest? LastRequest { get; private set; }

        public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return GenerateAsync(new AiImageGenerationRequest(prompt, AspectRatioPreset.Square1x1, 1024, 1024), cancellationToken);
        }

        public Task<GeneratedImage> GenerateAsync(AiImageGenerationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new GeneratedImage(
                [1, 2, 3],
                "image/jpeg",
                "@cf/black-forest-labs/flux-1-schnell"));
        }
    }

    [Fact]
    public async Task Router_routes_to_cloudflare_by_default()
    {
        var fakeCf = new FakeGenerator();
        var router = new AiImageGeneratorRouter(fakeCf, Options.Create(new AiImageOptions()));

        var request = new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024);
        var result = await router.GenerateAsync(request);

        Assert.Equal(1, fakeCf.CallCount);
        Assert.Equal("@cf/black-forest-labs/flux-1-schnell", result.Model);
    }

    [Fact]
    public void Router_reports_only_implemented_provider_as_available()
    {
        var router = new AiImageGeneratorRouter(
            new FakeGenerator(),
            Options.Create(new AiImageOptions { RunPodEnabled = true }));

        Assert.True(router.IsProviderAvailable(AiImageProvider.CloudflareSchnell));
        Assert.False(router.IsProviderAvailable(AiImageProvider.RunPodFluxDev));
    }

    [Fact]
    public async Task Router_rejects_runpod_when_disabled_via_cost_guard()
    {
        var fakeCf = new FakeGenerator();
        var router = new AiImageGeneratorRouter(fakeCf, Options.Create(new AiImageOptions { RunPodEnabled = false }));

        var request = new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.GenerateAsync(request, AiImageProvider.RunPodFluxDev));

        Assert.Contains("Cost Guard", ex.Message);
        Assert.Equal(0, fakeCf.CallCount);
    }

    [Fact]
    public async Task Router_rejects_runpod_when_enabled_but_contract_unverified()
    {
        var fakeCf = new FakeGenerator();
        var router = new AiImageGeneratorRouter(fakeCf, Options.Create(new AiImageOptions { RunPodEnabled = true }));

        var request = new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.GenerateAsync(request, AiImageProvider.RunPodFluxDev));

        Assert.Contains("adapter chưa được đăng ký", ex.Message);
        Assert.Equal(0, fakeCf.CallCount);
    }
}
