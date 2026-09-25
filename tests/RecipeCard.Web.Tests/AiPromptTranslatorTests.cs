using System.Net;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiPromptTranslatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TranslateVietnameseToEnglishAsync_rejects_empty_prompt(string? prompt)
    {
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-account-id",
            ApiToken = "test-token"
        });

        var fakeHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(fakeHandler);
        IAiPromptTranslator translator = new CloudflareWorkersAiPromptTranslator(httpClient, options);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            translator.TranslateVietnameseToEnglishAsync(prompt!));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
