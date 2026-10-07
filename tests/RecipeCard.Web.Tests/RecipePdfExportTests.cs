using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Recipes;
using RecipeCard.Web.Pdf;

namespace RecipeCard.Web.Tests;

public sealed class RecipePdfExportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RecipeDbContext> _options;
    private readonly string _testRoot;
    private readonly FakeWebHostEnvironment _env;
    private readonly RecipeCard.Web.Services.IRecipePdfModelFactory _factory;

    public RecipePdfExportTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new RecipeDbContext(_options);
        context.Database.EnsureCreated();

        _testRoot = Path.Combine(Path.GetTempPath(), $"RecipeCardPdfTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testRoot, "uploads", "steps"));
        _env = new FakeWebHostEnvironment { WebRootPath = _testRoot };

        var httpFactory = new FakeHttpClientFactory();
        var loader = new RecipeCard.Web.Services.PdfMediaLoader(
            httpFactory,
            _env,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeCard.Web.Services.PdfMediaLoader>.Instance);
        _factory = new RecipeCard.Web.Services.RecipePdfModelFactory(
            loader,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeCard.Web.Services.RecipePdfModelFactory>.Instance);
    }
    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task Export_recipe_with_zero_ingredients_is_rejected()
    {
        await using var db = new RecipeDbContext(_options);
        var recipe = new Recipe
        {
            Name = "Trà trống",
            Steps = [new RecipeStep { SortOrder = 1, Instruction = "Rót nước" }]
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new PreviewModel(db, _factory);
        var result = await page.OnGetPdfAsync(recipe.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Công thức chưa đủ điều kiện xuất PDF. Cần có ít nhất 1 nguyên liệu và 1 bước thực hiện.", page.ErrorMessage);
    }

    [Fact]
    public async Task Export_recipe_with_zero_steps_is_rejected()
    {
        await using var db = new RecipeDbContext(_options);
        var ing = new Ingredient { Name = "Cốt trà", NormalizedName = Ingredient.Normalize("Cốt trà"), DefaultUnit = "ml" };
        var recipe = new Recipe
        {
            Name = "Trà chưa có bước",
            Ingredients = [new RecipeIngredient { Ingredient = ing, Quantity = 100 }]
        };
        db.Ingredients.Add(ing);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new PreviewModel(db, _factory);
        var result = await page.OnGetPdfAsync(recipe.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Công thức chưa đủ điều kiện xuất PDF. Cần có ít nhất 1 nguyên liệu và 1 bước thực hiện.", page.ErrorMessage);
    }

    [Fact]
    public async Task Export_complete_recipe_generates_valid_pdf_bytes()
    {
        await using var db = new RecipeDbContext(_options);
        var ing1 = new Ingredient { Name = "Bột sữa Phê La", NormalizedName = Ingredient.Normalize("Bột sữa Phê La"), DefaultUnit = "g" };
        var ing2 = new Ingredient { Name = "Cốt trà Ô Long", NormalizedName = Ingredient.Normalize("Cốt trà Ô Long"), DefaultUnit = "ml" };

        var recipe = new Recipe
        {
            Name = "Ô Long Sữa Phê La Lạnh",
            GeneralNote = "Lắc can trà 10 nhịp trước khi đong. Phục vụ kèm ống hút to.",
            Ingredients =
            [
                new RecipeIngredient { Ingredient = ing1, Quantity = 22.5m },
                new RecipeIngredient { Ingredient = ing2, Quantity = 105m }
            ],
            Steps =
            [
                new RecipeStep { SortOrder = 1, Instruction = "Cho đá đầy miệng ly giấy." },
                new RecipeStep { SortOrder = 2, Instruction = "Đong bột sữa và cốt trà vào shaker, lắc đều 15-20 nhịp." }
            ]
        };

        db.Ingredients.AddRange(ing1, ing2);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new PreviewModel(db, _factory);
        var result = await page.OnGetPdfAsync(recipe.Id);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.EndsWith(".pdf", fileResult.FileDownloadName);

        var bytes = fileResult.FileContents;
        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > 1000);

        // Verify PDF Magic Header (%PDF-)
        var header = Encoding.ASCII.GetString(bytes, 0, 5);
        Assert.Equal("%PDF-", header);

        // Verify with real PDF parser that the document is valid, openable, and readable
        using var pdfDoc = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.True(pdfDoc.NumberOfPages >= 1);
        var firstPage = pdfDoc.GetPage(1);
        Assert.True(firstPage.Width > firstPage.Height);
        Assert.Contains(recipe.Name, firstPage.Text);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(20)]
    public void Adaptive_step_layouts_generate_valid_landscape_pdf(int stepCount)
    {
        var model = new RecipePdfModel
        {
            Title = $"Công thức {stepCount} bước",
            Ingredients =
            [
                new RecipePdfIngredientLine { Stt = 1, Name = "Trà", Quantity = 100, Unit = "ml" }
            ],
            Steps = Enumerable.Range(1, stepCount)
                .Select(stepNumber => new RecipePdfStepLine
                {
                    StepNumber = stepNumber,
                    Instruction = $"Thực hiện thao tác số {stepNumber}."
                })
                .ToList()
        };

        var bytes = new RecipePdfDocument(model).GeneratePdf();

        using var pdfDocument = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.True(pdfDocument.NumberOfPages >= 1);
        Assert.All(pdfDocument.GetPages(), page => Assert.True(page.Width > page.Height));
        var text = string.Join(' ', pdfDocument.GetPages().Select(page => page.Text));
        Assert.Contains($"Thực hiện thao tác số {stepCount}.", text);
        Assert.DoesNotContain("LƯU Ý & HƯỚNG DẪN PHỤC VỤ", text);
    }

    [Fact]
    public async Task Export_recipe_with_missing_image_on_disk_does_not_throw()
    {
        await using var db = new RecipeDbContext(_options);
        var ing = new Ingredient { Name = "Cà phê", NormalizedName = Ingredient.Normalize("Cà phê"), DefaultUnit = "ml" };
        var recipe = new Recipe
        {
            Name = "Cà phê đá",
            Ingredients = [new RecipeIngredient { Ingredient = ing, Quantity = 50 }],
            Steps =
            [
                new RecipeStep
                {
                    SortOrder = 1,
                    Instruction = "Pha cà phê",
                    ImageFileName = "non_existent_image_file.jpg"
                }
            ]
        };

        db.Ingredients.Add(ing);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new PreviewModel(db, _factory);
        var result = await page.OnGetPdfAsync(recipe.Id);

        var fileResult = Assert.IsType<FileContentResult>(result);
        var header = Encoding.ASCII.GetString(fileResult.FileContents, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    private sealed class FakeHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new();
    }

}
