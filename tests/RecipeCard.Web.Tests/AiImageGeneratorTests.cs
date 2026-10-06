using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiImageGeneratorTests
{
    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task GenerateAsync_returns_valid_image_on_success()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "cf-secret-token"
        });

        HttpRequestMessage? capturedRequest = null;
        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ValidJpegBytes)
            };
        });

        var httpClient = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        var result = await generator.GenerateAsync("A cup of oolong tea with ice");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(ValidJpegBytes, result.ImageBytes);
        Assert.Equal("@cf/black-forest-labs/flux-2-klein-4b", result.Model);

        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal("cf-secret-token", capturedRequest.Headers.Authorization?.Parameter);
        Assert.Contains("cf-acc-id", capturedRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GenerateAsync_returns_valid_image_when_response_is_json_with_result_image_base64()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "cf-secret-token"
        });

        var base64Image = Convert.ToBase64String(ValidJpegBytes);
        var jsonPayload = $"{{\"result\":{{\"image\":\"{base64Image}\"}},\"success\":true,\"errors\":[],\"messages\":[]}}";

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        var result = await generator.GenerateAsync("A cup of oolong tea with ice");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(ValidJpegBytes, result.ImageBytes);
        Assert.Equal("@cf/black-forest-labs/flux-2-klein-4b", result.Model);
    }

    [Fact]
    public async Task GenerateAsync_returns_valid_image_when_response_is_json_with_data_uri()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "cf-secret-token"
        });

        var base64Image = Convert.ToBase64String(ValidJpegBytes);
        var jsonPayload = $"{{\"result\":{{\"image\":\"data:image/jpeg;base64,{base64Image}\"}},\"success\":true}}";

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        var result = await generator.GenerateAsync("A cup of oolong tea with ice");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(ValidJpegBytes, result.ImageBytes);
    }

    [Fact]
    public async Task GenerateAsync_returns_valid_image_when_response_is_json_with_root_image()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "cf-secret-token"
        });

        var base64Image = Convert.ToBase64String(ValidJpegBytes);
        var jsonPayload = $"{{\"image\":\"{base64Image}\"}}";

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        var result = await generator.GenerateAsync("A cup of oolong tea with ice");

        Assert.NotNull(result);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(ValidJpegBytes, result.ImageBytes);
    }

    [Fact]
    public async Task GenerateAsync_throws_when_json_response_has_empty_or_missing_image()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "cf-secret-token"
        });

        var jsonPayload = "{\"result\":{},\"success\":true}";

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync("any prompt"));
        Assert.Contains("không đúng định dạng", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_throws_and_does_not_leak_secret_on_api_error()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "super-secret-token"
        });

        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"success\":false,\"errors\":[{\"code\":1000,\"message\":\"Quota exceeded\"}]}", Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync("any prompt"));

        Assert.Contains("Quota exceeded", ex.Message);
        Assert.DoesNotContain("super-secret-token", ex.Message);
        Assert.DoesNotContain("cf-acc-id", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_rejects_empty_prompt()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc-id",
            ApiToken = "cf-token"
        });

        var httpClient = new HttpClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var generator = new CloudflareWorkersAiImageGenerator(httpClient, options, new ImageValidator());

        await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => generator.GenerateAsync("   "));
    }
}
