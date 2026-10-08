using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class RunPodAiImageGeneratorTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    private class FakeValidator : IImageValidator
    {
        public ValidatedImageInfo Validate(Microsoft.AspNetCore.Http.IFormFile file) => new(".png", "image/png", 100);
        public ValidatedImageInfo Validate(Stream stream, string fileName, long length) => new(".png", "image/png", length);
        public ValidatedImageInfo ValidateBytes(byte[] bytes, string? suggestedFileName = null) => new(".png", "image/png", bytes.Length);
    }

    [Fact]
    public async Task Throws_when_endpoint_or_key_missing()
    {
        var options = Options.Create(new RunPodOptions { EndpointId = "", ApiKey = "" });
        var generator = new RunPodAiImageGenerator(
            new HttpClient(),
            options,
            new FakeValidator(),
            NullLogger<RunPodAiImageGenerator>.Instance);

        var request = new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(request));

        Assert.Contains("EndpointId chưa được cấu hình", ex.Message);
    }

    [Fact]
    public async Task Completes_and_parses_base64_image_from_runpod_completed_job()
    {
        var runJobId = "job-12345";
        var mockHandler = new MockHttpMessageHandler();
        var callCount = 0;

        // Mock PNG bytes (8 bytes)
        var fakePngBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var fakeB64 = Convert.ToBase64String(fakePngBytes);

        mockHandler.Handler = req =>
        {
            callCount++;
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/run"))
            {
                var envelope = new { id = runJobId, status = "IN_QUEUE" };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope))
                };
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/status/{runJobId}"))
            {
                var envelope = new
                {
                    id = runJobId,
                    status = "COMPLETED",
                    output = new { image = $"data:image/png;base64,{fakeB64}" }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new RunPodOptions
        {
            EndpointId = "test-endpoint",
            ApiKey = "test-key",
            PollIntervalMilliseconds = 10,
            RequestTimeoutSeconds = 5
        });

        var generator = new RunPodAiImageGenerator(
            httpClient,
            options,
            new FakeValidator(),
            NullLogger<RunPodAiImageGenerator>.Instance);

        var request = new AiImageGenerationRequest("a coffee cup", AspectRatioPreset.Square1x1, 1024, 1024);
        var result = await generator.GenerateAsync(request);

        Assert.NotNull(result);
        Assert.Equal("runpod/flux-1-dev", result.Model);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(fakePngBytes, result.ImageBytes);
    }

    [Fact]
    public async Task Completes_and_parses_real_verified_runpod_fixture_with_b64_json()
    {
        var runJobId = "job-real-flux";
        var mockHandler = new MockHttpMessageHandler();
        var fakePngBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var fakeB64 = Convert.ToBase64String(fakePngBytes);

        mockHandler.Handler = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/run"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "IN_QUEUE" }))
                };
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/status/{runJobId}"))
            {
                // Cấu trúc thực tế từ endpoint RunPod: { b64_json: "...", revised_prompt: "...", url: null }
                var envelope = new
                {
                    id = runJobId,
                    status = "COMPLETED",
                    output = new
                    {
                        b64_json = fakeB64,
                        revised_prompt = "A cup of Phê La tea on wooden table",
                        url = (string?)null
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(envelope))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var generator = new RunPodAiImageGenerator(
            new HttpClient(mockHandler),
            Options.Create(new RunPodOptions { EndpointId = "test", ApiKey = "test", PollIntervalMilliseconds = 10, RequestTimeoutSeconds = 2 }),
            new FakeValidator(),
            NullLogger<RunPodAiImageGenerator>.Instance);

        var result = await generator.GenerateAsync(new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024));

        Assert.NotNull(result);
        Assert.Equal("runpod/flux-1-dev", result.Model);
        Assert.Equal(fakePngBytes, result.ImageBytes);
    }

    [Fact]
    public async Task Throws_when_job_status_is_failed()
    {
        var runJobId = "job-fail-1";
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.Handler = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/run"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "IN_QUEUE" }))
                };
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/status/{runJobId}"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "FAILED", error = "Out of memory" }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var generator = new RunPodAiImageGenerator(
            new HttpClient(mockHandler),
            Options.Create(new RunPodOptions { EndpointId = "test", ApiKey = "test", PollIntervalMilliseconds = 10, RequestTimeoutSeconds = 2 }),
            new FakeValidator(),
            NullLogger<RunPodAiImageGenerator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024)));

        Assert.Contains("thất bại (Trạng thái: FAILED)", ex.Message);
    }

    [Fact]
    public async Task Throws_when_image_url_is_insecure_http()
    {
        var runJobId = "job-insecure-http";
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.Handler = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/run"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "IN_QUEUE" }))
                };
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/status/{runJobId}"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "COMPLETED", output = "http://example.com/image.png" }))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var generator = new RunPodAiImageGenerator(
            new HttpClient(mockHandler),
            Options.Create(new RunPodOptions { EndpointId = "test", ApiKey = "test", PollIntervalMilliseconds = 10, RequestTimeoutSeconds = 2 }),
            new FakeValidator(),
            NullLogger<RunPodAiImageGenerator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024)));

        Assert.Contains("HTTPS", ex.Message);
    }

    [Fact]
    public async Task Throws_when_image_url_is_redirect()
    {
        var runJobId = "job-redirect";
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.Handler = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.ToString().Contains("/run"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "IN_QUEUE" }))
                };
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains($"/status/{runJobId}"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = runJobId, status = "COMPLETED", output = "https://example.com/image.png" }))
                };
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.ToString().Contains("example.com"))
            {
                var redirectResp = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirectResp.Headers.Location = new Uri("https://127.0.0.1/evil.png");
                return redirectResp;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var generator = new RunPodAiImageGenerator(
            new HttpClient(mockHandler),
            Options.Create(new RunPodOptions { EndpointId = "test", ApiKey = "test", PollIntervalMilliseconds = 10, RequestTimeoutSeconds = 2 }),
            new FakeValidator(),
            NullLogger<RunPodAiImageGenerator>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateAsync(new AiImageGenerationRequest("prompt", AspectRatioPreset.Square1x1, 1024, 1024)));

        Assert.Contains("Redirect disallowed", ex.Message);
    }
}
