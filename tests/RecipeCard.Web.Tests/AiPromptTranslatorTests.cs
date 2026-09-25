using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiPromptTranslatorTests
{
    private const string TestAccountId = "cf-account-id";
    private const string TestApiToken = "test-token";
    private const string VietnameseInput = "Đong sữa dừa vào ly có đá.";
    private const string ExpectedEnglish = "Pour coconut milk into a glass with ice.";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TranslateVietnameseToEnglishAsync_rejects_empty_prompt(string? prompt)
    {
        var translator = CreateTranslator(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            translator.TranslateVietnameseToEnglishAsync(prompt!));
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_with_llama_sends_chat_messages_and_handles_response()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var translator = CreateTranslator(async req =>
        {
            capturedRequest = req;
            if (req.Content != null)
            {
                capturedBody = await req.Content.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"result":{"response":"Pour coconut milk into a glass with ice."}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedEnglish, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal(
            $"https://api.cloudflare.com/client/v4/accounts/{TestAccountId}/ai/run/@cf/meta/llama-3.1-8b-instruct",
            capturedRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal(TestApiToken, capturedRequest.Headers.Authorization?.Parameter);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("messages", out var messages));
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("beverage", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal(VietnameseInput, messages[1].GetProperty("content").GetString());
        Assert.Equal(150, root.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_with_llama_sanitizes_surrounding_quotes()
    {
        var translator = CreateTranslator(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"result":{"response":"\"Pour coconut milk into a glass with ice.\""}}""",
                System.Text.Encoding.UTF8,
                "application/json")
        });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);
        Assert.Equal(ExpectedEnglish, result);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_with_m2m100_sends_correct_outbound_request_and_handles_object_result()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var translator = CreateTranslator(async req =>
        {
            capturedRequest = req;
            if (req.Content != null)
            {
                capturedBody = await req.Content.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"result":{"translated_text":"Pour coconut milk into a glass with ice."}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }, model: "@cf/meta/m2m100-1.2b");

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedEnglish, result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal(
            $"https://api.cloudflare.com/client/v4/accounts/{TestAccountId}/ai/run/@cf/meta/m2m100-1.2b",
            capturedRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization?.Scheme);
        Assert.Equal(TestApiToken, capturedRequest.Headers.Authorization?.Parameter);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        Assert.Equal(VietnameseInput, doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("vi", doc.RootElement.GetProperty("source_lang").GetString());
        Assert.Equal("en", doc.RootElement.GetProperty("target_lang").GetString());
    }
    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_handles_array_result()
    {
        var translator = CreateTranslator(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"result":[{"translated_text":"Pour coconut milk into a glass with ice."}]}""",
                System.Text.Encoding.UTF8,
                "application/json")
        });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedEnglish, result);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_throws_on_api_error_without_leaking_credentials_or_prompt()
    {
        var translator = CreateTranslator(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"success":false,"errors":[{"code":1000,"message":"Model execution failed"}]}""",
                System.Text.Encoding.UTF8,
                "application/json")
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Contains("Lỗi từ Cloudflare AI: Model execution failed", ex.Message);
        Assert.DoesNotContain(TestAccountId, ex.Message);
        Assert.DoesNotContain(TestApiToken, ex.Message);
        Assert.DoesNotContain(VietnameseInput, ex.Message);
    }

    [Theory]
    [InlineData("""{"result":{"translated_text":""}}""")]
    [InlineData("""{"result":{"translated_text":"   "}}""")]
    [InlineData("""{"result":{}}""")]
    [InlineData("""{"result":[{"translated_text":""}]}""")]
    [InlineData("""{"result":[]}""")]
    [InlineData("""{"result":null}""")]
    [InlineData("""{"not_result":"test"}""")]
    [InlineData("not a json")]
    public async Task TranslateVietnameseToEnglishAsync_throws_on_missing_or_blank_translated_text(string responseJson)
    {
        var translator = CreateTranslator(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.DoesNotContain(TestAccountId, ex.Message);
        Assert.DoesNotContain(TestApiToken, ex.Message);
        Assert.DoesNotContain(VietnameseInput, ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_throws_on_network_failure()
    {
        var fakeHandler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(fakeHandler);
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = TestAccountId,
            ApiToken = TestApiToken
        });
        var translator = new CloudflareWorkersAiPromptTranslator(httpClient, options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Contains("Không thể kết nối", ex.Message);
        Assert.DoesNotContain(TestAccountId, ex.Message);
        Assert.DoesNotContain(TestApiToken, ex.Message);
        Assert.DoesNotContain(VietnameseInput, ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_preserves_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var fakeHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(fakeHandler);
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = TestAccountId,
            ApiToken = TestApiToken
        });
        var translator = new CloudflareWorkersAiPromptTranslator(httpClient, options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput, cts.Token));
    }


    private static IAiPromptTranslator CreateTranslator(Func<HttpRequestMessage, HttpResponseMessage> handler, string? model = null)
    {
        return CreateTranslator(req => Task.FromResult(handler(req)), model);
    }

    private static IAiPromptTranslator CreateTranslator(Func<HttpRequestMessage, Task<HttpResponseMessage>> asyncHandler, string? model = null)
    {
        var fakeHandler = new AsyncFakeHttpMessageHandler(asyncHandler);
        var httpClient = new HttpClient(fakeHandler);
        var options = Options.Create(new CloudflareOptions
        {
            AccountId = TestAccountId,
            ApiToken = TestApiToken,
            TranslationModel = model ?? "@cf/meta/llama-3.1-8b-instruct"
        });

        return new CloudflareWorkersAiPromptTranslator(httpClient, options);
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class AsyncFakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(request);
        }
    }
}
