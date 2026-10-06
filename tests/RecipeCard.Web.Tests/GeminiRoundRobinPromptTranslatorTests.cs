using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class GeminiRoundRobinPromptTranslatorTests
{
    private const string VietnameseInput = "Đong sữa dừa vào ly có đá.";
    private const string ExpectedGeminiEnglish = "Pour coconut milk into a glass with ice cubes.";
    private const string ExpectedCloudflareEnglish = "Cloudflare fallback translation.";

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TranslateVietnameseToEnglishAsync_rejects_empty_prompt(string? prompt)
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK),
            apiKeys: ["key-1"]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            translator.TranslateVietnameseToEnglishAsync(prompt!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task TranslateVietnameseToEnglishAsync_throws_on_invalid_key_count(int keyCount)
    {
        var keys = Enumerable.Range(1, keyCount).Select(i => $"key-{i}").ToList();
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK),
            apiKeys: keys);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Contains("Cấu hình Gemini chưa hợp lệ", ex.Message);
        Assert.DoesNotContain("key-", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TranslateVietnameseToEnglishAsync_throws_on_whitespace_key(string badKey)
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK),
            apiKeys: ["valid-key", badKey]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Contains("Cấu hình Gemini chưa hợp lệ", ex.Message);
        Assert.DoesNotContain("valid-key", ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_throws_on_duplicate_keys()
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK),
            apiKeys: ["key-1", "key-2", "key-1"]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Contains("Cấu hình Gemini chưa hợp lệ", ex.Message);
        Assert.Contains("trùng lặp", ex.Message);
        Assert.DoesNotContain("key-1", ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_first_attempt_success_sends_correct_request_and_does_not_call_cloudflare()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var cloudflareCalled = false;

        var translator = CreateAsyncTranslator(
            geminiAsyncHandler: async req =>
            {
                capturedRequest = req;
                if (req.Content != null)
                {
                    capturedBody = await req.Content.ReadAsStringAsync();
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "candidates": [
                            {
                              "content": {
                                "parts": [
                                  { "text": "Pour coconut milk into a glass with ice cubes." }
                                ]
                              }
                            }
                          ]
                        }
                        """,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            },
            apiKeys: ["k1", "k2", "k3"],
            cloudflareAsyncHandler: _ =>
            {
                cloudflareCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedGeminiEnglish, result);
        Assert.False(cloudflareCalled);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
            capturedRequest.RequestUri?.ToString());
        Assert.True(capturedRequest.Headers.TryGetValues("x-goog-api-key", out var keyValues));
        Assert.Equal("k1", keyValues.First());

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("systemInstruction", out var sysInst));
        Assert.Contains("beverage", sysInst.GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.True(root.TryGetProperty("contents", out var contents));
        Assert.Equal("user", contents[0].GetProperty("role").GetString());
        Assert.Equal(VietnameseInput, contents[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(150, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_sanitizes_quotes_and_markdown()
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "text": "\"```Pour coconut milk into a glass with ice cubes.```\"" }
                            ]
                          }
                        }
                      ]
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
            apiKeys: ["k1"]);

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);
        Assert.Equal(ExpectedGeminiEnglish, result);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_ignores_thought_parts()
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "candidates": [
                        {
                          "content": {
                            "parts": [
                              { "thought": true, "text": "Thinking process here..." },
                              { "text": "Pour coconut milk into a glass with ice cubes." }
                            ]
                          }
                        }
                      ]
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
            apiKeys: ["k1"]);

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);
        Assert.Equal(ExpectedGeminiEnglish, result);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_rotates_on_429_and_stops_on_first_success()
    {
        var usedKeys = new List<string>();

        var translator = CreateTranslator(
            geminiHandler: req =>
            {
                var key = req.Headers.GetValues("x-goog-api-key").First();
                usedKeys.Add(key);

                if (key == "k1")
                {
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "candidates": [
                            {
                              "content": {
                                "parts": [
                                  { "text": "Pour coconut milk into a glass with ice cubes." }
                                ]
                              }
                            }
                          ]
                        }
                        """,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            },
            apiKeys: ["k1", "k2", "k3"]);

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedGeminiEnglish, result);
        Assert.Equal(["k1", "k2"], usedKeys);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_consecutive_requests_advance_cursor()
    {
        var cursor = new GeminiKeyCursor();
        var capturedKeysFirstCall = new List<string>();
        var capturedKeysSecondCall = new List<string>();

        var translator1 = CreateTranslator(
            geminiHandler: req =>
            {
                var k = req.Headers.GetValues("x-goog-api-key").First();
                capturedKeysFirstCall.Add(k);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"candidates":[{"content":{"parts":[{"text":"Result 1"}]}}]}""",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            },
            apiKeys: ["k1", "k2", "k3"],
            cursor: cursor);

        var translator2 = CreateTranslator(
            geminiHandler: req =>
            {
                var k = req.Headers.GetValues("x-goog-api-key").First();
                capturedKeysSecondCall.Add(k);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"candidates":[{"content":{"parts":[{"text":"Result 2"}]}}]}""",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            },
            apiKeys: ["k1", "k2", "k3"],
            cursor: cursor);

        await translator1.TranslateVietnameseToEnglishAsync(VietnameseInput);
        await translator2.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(["k1"], capturedKeysFirstCall);
        Assert.Equal(["k2"], capturedKeysSecondCall);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_full_15_calls_429_with_3_keys_triggers_cloudflare_fallback_once()
    {
        var usedKeys = new List<string>();
        var cloudflareCallCount = 0;

        var translator = CreateTranslator(
            geminiHandler: req =>
            {
                var key = req.Headers.GetValues("x-goog-api-key").First();
                usedKeys.Add(key);
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            },
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"result\":{{\"response\":\"{ExpectedCloudflareEnglish}\"}}}}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedCloudflareEnglish, result);
        Assert.Equal(15, usedKeys.Count);
        Assert.Equal(1, cloudflareCallCount);

        var expectedSequence = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            expectedSequence.AddRange(["k1", "k2", "k3"]);
        }
        Assert.Equal(expectedSequence, usedKeys);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 15)]
    public async Task TranslateVietnameseToEnglishAsync_exact_5_rounds_per_key_count(int keyCount, int expectedAttempts)
    {
        var usedKeys = new List<string>();
        var keys = Enumerable.Range(1, keyCount).Select(i => $"k{i}").ToList();
        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: req =>
            {
                var key = req.Headers.GetValues("x-goog-api-key").First();
                usedKeys.Add(key);
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            },
            apiKeys: keys,
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"result\":{{\"response\":\"{ExpectedCloudflareEnglish}\"}}}}",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedCloudflareEnglish, result);
        Assert.True(cloudflareCalled);
        Assert.Equal(expectedAttempts, usedKeys.Count);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_success_on_attempt_15_does_not_call_cloudflare()
    {
        var attemptCount = 0;
        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: _ =>
            {
                attemptCount++;
                if (attemptCount < 15)
                {
                    return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "candidates": [
                            {
                              "content": {
                                "parts": [
                                  { "text": "Pour coconut milk into a glass with ice cubes." }
                                ]
                              }
                            }
                          ]
                        }
                        """,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            },
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var result = await translator.TranslateVietnameseToEnglishAsync(VietnameseInput);

        Assert.Equal(ExpectedGeminiEnglish, result);
        Assert.Equal(15, attemptCount);
        Assert.False(cloudflareCalled);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TranslateVietnameseToEnglishAsync_non_429_fails_immediately_without_rotation_or_fallback(HttpStatusCode statusCode)
    {
        var geminiCallCount = 0;
        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: _ =>
            {
                geminiCallCount++;
                return new HttpResponseMessage(statusCode);
            },
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Equal(1, geminiCallCount);
        Assert.False(cloudflareCalled);
        Assert.Contains("Gemini AI không thể xử lý yêu cầu lúc này", ex.Message);
        Assert.DoesNotContain("k1", ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_network_failure_fails_immediately_without_fallback()
    {
        var geminiCallCount = 0;
        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: _ =>
            {
                geminiCallCount++;
                throw new HttpRequestException("Network failure");
            },
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Equal(1, geminiCallCount);
        Assert.False(cloudflareCalled);
        Assert.Contains("Không thể kết nối đến dịch vụ Gemini AI", ex.Message);
    }

    [Theory]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("""{"candidates":[{"content":{"parts":[]}}]}""")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":""}]}}]}""")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"text":"   "}]}}]}""")]
    [InlineData("""{"error":"something"}""")]
    [InlineData("not json")]
    public async Task TranslateVietnameseToEnglishAsync_blank_or_invalid_response_fails_immediately(string responseJson)
    {
        var geminiCallCount = 0;
        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: _ =>
            {
                geminiCallCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
                };
            },
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Equal(1, geminiCallCount);
        Assert.False(cloudflareCalled);
        Assert.Contains("không trả về kết quả dịch hợp lệ", ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_cancellation_preserves_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.OK),
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput, cts.Token));

        Assert.False(cloudflareCalled);
    }
    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_timeout_converts_to_InvalidOperationException_and_does_not_fallback()
    {
        var cloudflareCalled = false;

        var translator = CreateTranslator(
            geminiHandler: _ => throw new OperationCanceledException(new CancellationToken(canceled: true)),
            apiKeys: ["k1", "k2", "k3"],
            cloudflareHandler: _ =>
            {
                cloudflareCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput, CancellationToken.None));

        Assert.Contains("quá thời gian chờ", ex.Message);
        Assert.False(cloudflareCalled);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_cloudflare_fallback_error_does_not_leak_provider_secret_message()
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            apiKeys: ["k1"],
            cloudflareHandler: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    """{"success":false,"errors":[{"code":5000,"message":"secret upstream error detail"}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Equal("Dịch vụ AI không thể xử lý yêu cầu lúc này.", ex.Message);
        Assert.DoesNotContain("secret upstream error detail", ex.Message);
    }

    [Fact]
    public async Task TranslateVietnameseToEnglishAsync_cloudflare_fallback_error_bubbles_up()
    {
        var translator = CreateTranslator(
            geminiHandler: _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            apiKeys: ["k1"],
            cloudflareHandler: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    """{"success":false,"errors":[{"code":5000,"message":"Cloudflare down"}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            translator.TranslateVietnameseToEnglishAsync(VietnameseInput));

        Assert.Equal("Dịch vụ AI không thể xử lý yêu cầu lúc này.", ex.Message);
    }

    private static GeminiRoundRobinPromptTranslator CreateTranslator(
        Func<HttpRequestMessage, HttpResponseMessage> geminiHandler,
        List<string> apiKeys,
        Func<HttpRequestMessage, HttpResponseMessage>? cloudflareHandler = null,
        GeminiKeyCursor? cursor = null)
    {
        return CreateAsyncTranslator(
            req => Task.FromResult(geminiHandler(req)),
            apiKeys,
            cloudflareHandler != null ? req => Task.FromResult(cloudflareHandler(req)) : null,
            cursor);
    }

    private static GeminiRoundRobinPromptTranslator CreateAsyncTranslator(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> geminiAsyncHandler,
        List<string> apiKeys,
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? cloudflareAsyncHandler = null,
        GeminiKeyCursor? cursor = null)
    {
        var geminiHttpClient = new HttpClient(new AsyncFakeHttpMessageHandler(geminiAsyncHandler));

        var cfHandler = cloudflareAsyncHandler ?? (_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var cfHttpClient = new HttpClient(new AsyncFakeHttpMessageHandler(cfHandler));
        var cfOptions = Options.Create(new CloudflareOptions
        {
            AccountId = "cf-account",
            ApiToken = "cf-token"
        });
        var cfTranslator = new CloudflareWorkersAiPromptTranslator(cfHttpClient, cfOptions);

        var geminiOptions = Options.Create(new GeminiOptions
        {
            ApiKeys = apiKeys
        });

        return new GeminiRoundRobinPromptTranslator(
            geminiHttpClient,
            cfTranslator,
            cursor ?? new GeminiKeyCursor(),
            geminiOptions);
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
