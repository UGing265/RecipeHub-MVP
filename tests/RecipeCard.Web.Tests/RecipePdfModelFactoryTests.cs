using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pdf;
using RecipeCard.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace RecipeCard.Web.Tests;

public sealed class RecipePdfModelFactoryTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FakeWebHostEnvironment _env;

    public RecipePdfModelFactoryTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"FactoryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testRoot, "uploads", "steps"));
        _env = new FakeWebHostEnvironment { WebRootPath = _testRoot };
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private static (PdfMediaLoader loader, RecipePdfModelFactory factory) CreateServices(
        IHttpClientFactory httpFactory,
        IWebHostEnvironment env,
        IPdfMediaObserver? observer = null)
    {
        var loader = new PdfMediaLoader(
            httpFactory,
            env,
            NullLogger<PdfMediaLoader>.Instance,
            observer);
        var factory = new RecipePdfModelFactory(
            loader,
            NullLogger<RecipePdfModelFactory>.Instance);
        return (loader, factory);
    }

    private static byte[] CreateValidJpegBytes(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    private static byte[] CreateMultiFrameGifBytes(int width, int height, int frameCount)
    {
        using var image = new Image<Rgba32>(width, height);
        for (int i = 1; i < frameCount; i++)
        {
            using var frame = new Image<Rgba32>(width, height);
            image.Frames.AddFrame(frame.Frames[0]);
        }
        using var ms = new MemoryStream();
        image.Save(ms, new GifEncoder());
        return ms.ToArray();
    }

    [Fact]
    public async Task CreateAsync_maps_ingredients_steps_and_media_in_input_order()
    {
        var validBytes = CreateValidJpegBytes(100, 100);
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(validBytes)
        }));
        var httpClient = new HttpClient(handler);
        var httpFactory = new SingleClientFactory(httpClient);
        var (_, factory) = CreateServices(httpFactory, _env);

        var ing1 = new Ingredient { Name = "Đường", DefaultUnit = "g" };
        var ing2 = new Ingredient { Name = "Sữa", DefaultUnit = "ml" };

        var recipe1 = new Recipe
        {
            Id = 10,
            Name = "Món 10",
            GeneralNote = "Ghi chú 10",
            FinalMediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/hero10.jpg" },
            Ingredients =
            [
                new RecipeIngredient { Ingredient = ing1, Quantity = 10 },
                new RecipeIngredient { Ingredient = ing2, Quantity = 20 }
            ],
            Steps =
            [
                new RecipeStep { Id = 101, SortOrder = 1, Instruction = "Bước 1" },
                new RecipeStep { Id = 102, SortOrder = 2, Instruction = "Bước 2", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/step102.jpg", SourceType = MediaSourceType.AiIllustration } }
            ]
        };

        var recipe2 = new Recipe
        {
            Id = 5,
            Name = "Món 5",
            GeneralNote = "Ghi chú 5",
            Ingredients = [new RecipeIngredient { Ingredient = ing2, Quantity = 50 }],
            Steps = [new RecipeStep { Id = 51, SortOrder = 1, Instruction = "Bước món 5" }]
        };

        await using var batch = await factory.CreateAsync([recipe1, recipe2], CancellationToken.None);

        Assert.Equal(2, batch.Models.Count);
        Assert.Equal("Món 10", batch.Models[0].Title);
        Assert.Equal("Ghi chú 10", batch.Models[0].GeneralNote);
        Assert.Equal(2, batch.Models[0].Ingredients.Count);
        Assert.Equal("Đường", batch.Models[0].Ingredients[0].Name);
        Assert.Equal("Sữa", batch.Models[0].Ingredients[1].Name);
        Assert.NotNull(batch.Models[0].HeroImageFullPath);
        Assert.True(File.Exists(batch.Models[0].HeroImageFullPath));
        Assert.True(batch.Models[0].Steps[1].IsAiIllustration);

        Assert.Equal("Món 5", batch.Models[1].Title);
        Assert.Null(batch.Models[1].HeroImageFullPath);
    }

    [Fact]
    public async Task Remote_media_never_exceeds_four_concurrent_requests()
    {
        var validBytes = CreateValidJpegBytes(100, 100);
        int activeRemote = 0;
        int peakRemote = 0;
        var lockObj = new object();

        var handler = new TestHttpMessageHandler(async _ =>
        {
            lock (lockObj)
            {
                activeRemote++;
                if (activeRemote > peakRemote) peakRemote = activeRemote;
            }
            await Task.Delay(50);
            lock (lockObj)
            {
                activeRemote--;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(validBytes)
            };
        });

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var steps = Enumerable.Range(1, 10).Select(i => new RecipeStep
        {
            Id = i,
            SortOrder = i,
            Instruction = $"Bước {i}",
            MediaAsset = new MediaAsset { DeliveryUrl = $"https://example.com/{i}.jpg" }
        }).ToList();

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Recipe with 10 remote images",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = steps
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        Assert.True(peakRemote <= 4, $"Peak remote concurrency was {peakRemote}, expected <= 4");
    }

    [Fact]
    public async Task Decode_resize_never_exceeds_two_concurrent_images()
    {
        int activeDecode = 0;
        int peakDecode = 0;
        var lockObj = new object();

        var observer = new DelegateMediaObserver(
            onDecodeStart: () =>
            {
                lock (lockObj)
                {
                    activeDecode++;
                    if (activeDecode > peakDecode) peakDecode = activeDecode;
                }
            },
            onDecodeFinish: () =>
            {
                lock (lockObj)
                {
                    activeDecode--;
                }
            });

        var validBytes = CreateValidJpegBytes(200, 200);
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(validBytes)
        }));

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env, observer);

        var steps = Enumerable.Range(1, 8).Select(i => new RecipeStep
        {
            Id = i,
            SortOrder = i,
            Instruction = $"Bước {i}",
            MediaAsset = new MediaAsset { DeliveryUrl = $"https://example.com/{i}.jpg" }
        }).ToList();

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Recipe with 8 images",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = steps
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        Assert.True(peakDecode <= 2, $"Peak decode concurrency was {peakDecode}, expected <= 2");
    }

    [Fact]
    public async Task Oversized_content_length_and_stream_without_length_are_omitted()
    {
        var oversizedBytes = new byte[8 * 1024 * 1024 + 100]; // > 8 MiB
        var handler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("with-header"))
            {
                var msg = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(oversizedBytes)
                };
                msg.Content.Headers.ContentLength = oversizedBytes.Length;
                return Task.FromResult(msg);
            }
            else
            {
                var msg = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(oversizedBytes))
                };
                msg.Content.Headers.ContentLength = null;
                return Task.FromResult(msg);
            }
        });

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Oversized images",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps =
            [
                new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/with-header.jpg" } },
                new RecipeStep { Id = 2, SortOrder = 2, Instruction = "Step 2", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/without-header.jpg" } }
            ]
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        Assert.Single(batch.Models);
        Assert.Null(batch.Models[0].Steps[0].ImageFullPath);
        Assert.Null(batch.Models[0].Steps[1].ImageFullPath);
    }

    [Fact]
    public async Task Image_over_dimension_or_pixel_limit_is_omitted_without_failing_recipe()
    {
        var wideImage = CreateValidJpegBytes(6001, 10);
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(wideImage)
        }));

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Over dimension image",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = [new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/wide.jpg" } }]
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        Assert.Single(batch.Models);
        Assert.Null(batch.Models[0].Steps[0].ImageFullPath);
    }

    [Fact]
    public async Task Animated_image_decodes_only_first_frame()
    {
        var animatedGif = CreateMultiFrameGifBytes(100, 100, 5);
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(animatedGif)
        }));

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Animated GIF",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = [new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/anim.gif" } }]
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        var path = batch.Models[0].Steps[0].ImageFullPath;
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        using var loaded = await Image.LoadAsync(path);
        Assert.Equal(1, loaded.Frames.Count);
    }

    [Fact]
    public async Task Hero_and_step_are_resized_within_their_boxes_and_output_cap()
    {
        var largeImage = CreateValidJpegBytes(2000, 2000);
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(largeImage)
        }));

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Large images",
            FinalMediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/hero.jpg" },
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = [new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/step.jpg" } }]
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        var heroPath = batch.Models[0].HeroImageFullPath;
        var stepPath = batch.Models[0].Steps[0].ImageFullPath;

        Assert.NotNull(heroPath);
        Assert.NotNull(stepPath);

        var heroInfo = await Image.IdentifyAsync(heroPath);
        Assert.True(heroInfo.Width <= 1200 && heroInfo.Height <= 750);
        Assert.True(new FileInfo(heroPath).Length <= 750 * 1024);

        var stepInfo = await Image.IdentifyAsync(stepPath);
        Assert.True(stepInfo.Width <= 900 && stepInfo.Height <= 600);
        Assert.True(new FileInfo(stepPath).Length <= 750 * 1024);
    }

    [Fact]
    public async Task Timed_out_or_invalid_remote_image_becomes_null()
    {
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Missing remote image",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = [new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/missing.jpg" } }]
        };

        await using var batch = await factory.CreateAsync([recipe], CancellationToken.None);

        Assert.Single(batch.Models);
        Assert.Null(batch.Models[0].Steps[0].ImageFullPath);
        Assert.Equal("Step 1", batch.Models[0].Steps[0].Instruction);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_temp_directory_is_removed()
    {
        using var cts = new CancellationTokenSource();
        var handler = new TestHttpMessageHandler(async _ =>
        {
            cts.Cancel();
            await Task.Delay(100);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(CreateValidJpegBytes(100, 100)) };
        });

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Cancelled recipe",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = [new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/1.jpg" } }]
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await factory.CreateAsync([recipe], cts.Token);
        });
    }

    [Fact]
    public async Task Disposing_batch_removes_all_normalized_files()
    {
        var validBytes = CreateValidJpegBytes(100, 100);
        var handler = new TestHttpMessageHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(validBytes)
        }));

        var httpFactory = new SingleClientFactory(new HttpClient(handler));
        var (_, factory) = CreateServices(httpFactory, _env);

        var recipe = new Recipe
        {
            Id = 1,
            Name = "Recipe for disposal test",
            Ingredients = [new RecipeIngredient { Ingredient = new Ingredient { Name = "A", DefaultUnit = "g" }, Quantity = 1 }],
            Steps = [new RecipeStep { Id = 1, SortOrder = 1, Instruction = "Step 1", MediaAsset = new MediaAsset { DeliveryUrl = "https://example.com/step.jpg" } }]
        };

        string? filePath;
        var batch = await factory.CreateAsync([recipe], CancellationToken.None);
        filePath = batch.Models[0].Steps[0].ImageFullPath;
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath));

        var dir = Path.GetDirectoryName(filePath);

        await batch.DisposeAsync();

        Assert.False(File.Exists(filePath));
        Assert.False(Directory.Exists(dir));
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class DelegateMediaObserver(
        Action? onRemoteStart = null,
        Action? onRemoteFinish = null,
        Action? onDecodeStart = null,
        Action? onDecodeFinish = null) : IPdfMediaObserver
    {
        public void OnRemoteRequestStarting() => onRemoteStart?.Invoke();
        public void OnRemoteRequestFinished() => onRemoteFinish?.Invoke();
        public void OnDecodeStarting() => onDecodeStart?.Invoke();
        public void OnDecodeFinished() => onDecodeFinish?.Invoke();
    }
}
