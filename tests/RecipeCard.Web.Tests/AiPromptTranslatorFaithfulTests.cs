using System.Net;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiPromptTranslatorFaithfulTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task Gemini_sends_shared_faithful_instruction_and_4096_max_output_tokens()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "text": "TARGET: STEP INSTRUCTION\nRECIPE: Oolong Tea" }
                            ]
                          }
                        }
                      ]
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new GeminiOptions { ApiKeys = ["key1"] });
        var cursor = new GeminiKeyCursor();
        var fallbackClient = new HttpClient(handler);
        var fallbackOptions = Options.Create(new CloudflareOptions { AccountId = "acc", ApiToken = "tok" });
        var fallbackTranslator = new CloudflareWorkersAiPromptTranslator(fallbackClient, fallbackOptions);

        var translator = new GeminiRoundRobinPromptTranslator(client, fallbackTranslator, cursor, options);
        var result = await translator.TranslateVietnameseToEnglishAsync("TARGET: BƯỚC\nRECIPE: Trà ô long");

        Assert.NotNull(capturedBody);
        using var doc = System.Text.Json.JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;

        var sysText = root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Equal(AiPromptTranslationConstants.FaithfulSystemInstruction, sysText);
        Assert.Equal(AiPromptTranslationConstants.MaxOutputTokens, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task Cloudflare_llama_sends_shared_faithful_instruction_and_4096_max_tokens()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new FakeHttpMessageHandler(req =>
        {
            capturedRequest = req;
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "result": {
                        "response": "TARGET: STEP INSTRUCTION\nRECIPE: Oolong Tea"
                      },
                      "success": true
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var client = new HttpClient(handler);
        var options = Options.Create(new CloudflareOptions { AccountId = "acc", ApiToken = "tok" });
        var translator = new CloudflareWorkersAiPromptTranslator(client, options);

        var result = await translator.TranslateVietnameseToEnglishAsync("TARGET: BƯỚC\nRECIPE: Trà ô long");

        Assert.NotNull(capturedBody);
        using var doc = System.Text.Json.JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;

        var messages = root.GetProperty("messages");
        var sysContent = messages[0].GetProperty("content").GetString();
        Assert.Equal(AiPromptTranslationConstants.FaithfulSystemInstruction, sysContent);
        Assert.Equal(AiPromptTranslationConstants.MaxOutputTokens, root.GetProperty("max_tokens").GetInt32());
    }
}
