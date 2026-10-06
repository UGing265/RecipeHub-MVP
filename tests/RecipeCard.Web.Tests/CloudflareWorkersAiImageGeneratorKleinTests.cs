using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class CloudflareWorkersAiImageGeneratorKleinTests
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

    [Theory]
    [InlineData(AspectRatioPreset.Square1x1, 1024, 1024)]
    [InlineData(AspectRatioPreset.StandardLandscape4x3, 1024, 768)]
    [InlineData(AspectRatioPreset.WideLandscape16x9, 1280, 720)]
    [InlineData(AspectRatioPreset.Portrait4x5, 768, 960)]
    public async Task GenerateAsync_with_preset_sends_multipart_with_exact_dimensions_and_no_steps(
        AspectRatioPreset preset, int expectedWidth, int expectedHeight)
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-acc",
            ApiToken = "cf-tok",
            Model = "@cf/black-forest-labs/flux-2-klein-4b"
        });

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var fakeHandler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(ValidJpegBytes)
            };
        });

        var client = new HttpClient(fakeHandler);
        var generator = new CloudflareWorkersAiImageGenerator(client, options, new ImageValidator());

        var (w, h) = preset.ToDimensions();
        var request = new AiImageGenerationRequest("A refreshing iced peach tea", preset, w, h);

        var result = await generator.GenerateAsync(request);

        Assert.NotNull(result);
        Assert.Equal("@cf/black-forest-labs/flux-2-klein-4b", result.Model);

        Assert.NotNull(capturedRequest);
        Assert.IsType<MultipartFormDataContent>(capturedRequest.Content);

        Assert.NotNull(capturedBody);
        Assert.Contains("name=prompt", capturedBody);
        Assert.Contains("A refreshing iced peach tea", capturedBody);
        Assert.Contains("name=width", capturedBody);
        Assert.Contains(expectedWidth.ToString(), capturedBody);
        Assert.Contains("name=height", capturedBody);
        Assert.Contains(expectedHeight.ToString(), capturedBody);
        Assert.DoesNotContain("name=steps", capturedBody);
    }
}
