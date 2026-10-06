using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class CloudflareWorkersAiImageGeneratorFluxTests
{
    private static readonly byte[] ValidJpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCABkAGQDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigAooooAKKKKACiiigAooooAKKKKACiiigD//2Q==");

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task GenerateAsync_with_flux_1_schnell_sends_json_payload_and_steps_4()
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc",
            ApiToken = "cf-tok",
            Model = "@cf/black-forest-labs/flux-1-schnell"
        });

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            var base64 = Convert.ToBase64String(ValidJpegBytes);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"result\":{{\"image\":\"{base64}\"}}}}", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(client, options, new ImageValidator());

        var request = new AiImageGenerationRequest("A refreshing iced peach tea", AspectRatioPreset.Square1x1, 1024, 1024);

        var result = await generator.GenerateAsync(request);

        Assert.NotNull(result);
        Assert.Equal("@cf/black-forest-labs/flux-1-schnell", result.Model);

        Assert.NotNull(capturedRequest);
        Assert.Equal("application/json", capturedRequest.Content?.Headers.ContentType?.MediaType);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;

        Assert.Equal("A refreshing iced peach tea", root.GetProperty("prompt").GetString());
        Assert.Equal(4, root.GetProperty("steps").GetInt32());
        Assert.False(root.TryGetProperty("seed", out _));
    }
}
