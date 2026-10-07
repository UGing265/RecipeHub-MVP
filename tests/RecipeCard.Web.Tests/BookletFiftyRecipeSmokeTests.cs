using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pdf;
using RecipeCard.Web.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace RecipeCard.Web.Tests;

public sealed class BookletFiftyRecipeSmokeTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FakeWebHostEnvironment _env;
    private readonly string _logoPath;
    private readonly RecipePdfModelFactory _factory;

    static BookletFiftyRecipeSmokeTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public BookletFiftyRecipeSmokeTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"FiftySmoke_{Guid.NewGuid():N}");
        var brandDir = Path.Combine(_testRoot, "images", "brand");
        Directory.CreateDirectory(brandDir);

        _logoPath = Path.Combine(brandDir, "phe-la-logo.svg");
        File.WriteAllText(_logoPath,
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <circle cx="50" cy="50" r="40" fill="#141414" />
            </svg>
            """);

        _env = new FakeWebHostEnvironment { WebRootPath = _testRoot };

        var loader = new PdfMediaLoader(
            new TestHttpClientFactory(),
            _env,
            NullLogger<PdfMediaLoader>.Instance);
        _factory = new RecipePdfModelFactory(loader, NullLogger<RecipePdfModelFactory>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private static byte[] Create3000x2000ImageBytes()
    {
        using var img = new Image<Rgb24>(3000, 2000);
        using var ms = new MemoryStream();
        img.Save(ms, new JpegEncoder { Quality = 75 });
        return ms.ToArray();
    }

    private static List<Recipe> CreateFiftyOneReadyRecipes(byte[] imageBytes)
    {
        var ing1 = new Ingredient { Name = "Cốt trà Phê La", DefaultUnit = "ml" };
        var ing2 = new Ingredient { Name = "Sữa đặc", DefaultUnit = "g" };

        var recipes = new List<Recipe>(51);
        for (int i = 51; i >= 1; i--) // ID DESC order
        {
            var recipe = new Recipe
            {
                Id = i,
                Name = $"SMOKE BOOKLET {i:00}",
                GeneralNote = $"Ghi chú công thức smoke thứ {i}",
                Ingredients =
                [
                    new RecipeIngredient { Ingredient = ing1, Quantity = 50 },
                    new RecipeIngredient { Ingredient = ing2, Quantity = 20 }
                ],
                Steps =
                [
                    new RecipeStep { Id = i * 10 + 1, SortOrder = 1, Instruction = $"Bước 1 công thức {i}" },
                    new RecipeStep { Id = i * 10 + 2, SortOrder = 2, Instruction = $"Bước 2 công thức {i}" }
                ]
            };
            recipes.Add(recipe);
        }

        return recipes;
    }

    [Fact]
    public async Task Smoke_fifty_recipes_two_consecutive_runs_within_thresholds()
    {
        var imageBytes = Create3000x2000ImageBytes();
        var allRecipes = CreateFiftyOneReadyRecipes(imageBytes);

        // Cap rule: select only 50 recipes (recipes 1 to 50 in ID DESC: ID 51 down to 2)
        var selectedFifty = allRecipes.Take(50).ToList();
        Assert.Equal(50, selectedFifty.Count);
        Assert.Equal(51, selectedFifty.First().Id);
        Assert.Equal(2, selectedFifty.Last().Id);

        // Run 1: Measure elapsed time, memory, output size and temp cleanup
        var (elapsed1, outputBytes1, peakWorkingSet1) = await RunFiftyExportAsync(selectedFifty, isRun1: true);
        Assert.True(elapsed1.TotalSeconds < 120, $"Run 1 took {elapsed1.TotalSeconds:F1}s (limit: 120s)");
        Assert.True(outputBytes1.Length < 80 * 1024 * 1024, $"Run 1 output size was {outputBytes1.Length / 1024 / 1024:F1}MB (limit: 80MB)");
        Assert.True(peakWorkingSet1 < 768 * 1024 * 1024, $"Run 1 working set {peakWorkingSet1 / 1024 / 1024}MB (limit: 768MB)");

        // Verify PDF integrity with PdfPig
        using (var pdf1 = UglyToad.PdfPig.PdfDocument.Open(outputBytes1))
        {
            Assert.True(pdf1.NumberOfPages >= 54); // 1 Cover + 4 TOC pages (50 items / 15) + 50 Recipe pages = 55 pages
            var page1 = pdf1.GetPage(1);
            Assert.Contains("BỘ HƯỚNG DẪN", page1.Text);
            Assert.Contains("Phòng đào tạo - Phê La", page1.Text);

            var page2 = pdf1.GetPage(2);
            Assert.Contains("MỤC LỤC", page2.Text);
            Assert.Contains("SMOKE BOOKLET 51", page2.Text);

            // Recipe 51 must start strictly after TOC
            var recipe1Text = pdf1.GetPage(6).Text; // Page 1 Cover + 4 TOC = 5 pages -> Page 6 is first recipe
            Assert.Contains("SMOKE BOOKLET 51", recipe1Text);
        }

        // Run 2: Consecutive run to detect memory retention or temp leaks
        var (elapsed2, outputBytes2, peakWorkingSet2) = await RunFiftyExportAsync(selectedFifty, isRun1: false);
        Assert.True(elapsed2.TotalSeconds < 120, $"Run 2 took {elapsed2.TotalSeconds:F1}s (limit: 120s)");
        Assert.True(outputBytes2.Length < 80 * 1024 * 1024, $"Run 2 output size was {outputBytes2.Length / 1024 / 1024:F1}MB (limit: 80MB)");
        Assert.True(peakWorkingSet2 < 768 * 1024 * 1024, $"Run 2 working set {peakWorkingSet2 / 1024 / 1024}MB (limit: 768MB)");

        // Memory check: both runs must be comfortably under 768MB limit (plan threshold)
        Assert.True(peakWorkingSet1 < 768 * 1024 * 1024);
        Assert.True(peakWorkingSet2 < 768 * 1024 * 1024);

        // Verify temp cleanup of the executed batches
        Assert.NotNull(tempDir1);
        Assert.False(Directory.Exists(tempDir1));
        Assert.NotNull(tempDir2);
        Assert.False(Directory.Exists(tempDir2));
    }

    private string? tempDir1;
    private string? tempDir2;

    private async Task<(TimeSpan Elapsed, byte[] OutputBytes, long PeakWorkingSet)> RunFiftyExportAsync(IReadOnlyList<Recipe> recipes, bool isRun1 = true)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sw = Stopwatch.StartNew();
        long peakMemory = 0;
        await using var batch = await _factory.CreateAsync(recipes, CancellationToken.None);
        if (isRun1) tempDir1 = batch.TempDirectory;
        else tempDir2 = batch.TempDirectory;

        var model = new RecipeBookletPdfModel(
            Recipes: batch.Models,
            ExportedAt: new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(7)),
            OfficialLogoSvgPath: _logoPath);
        var doc = new RecipeBookletPdfDocument(model);
        var bytes = doc.GeneratePdf();

        sw.Stop();

        using var process = Process.GetCurrentProcess();
        peakMemory = process.WorkingSet64;

        return (sw.Elapsed, bytes, peakMemory);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
