using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Recipes;
using RecipeCard.Web.Pdf;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Tests;

public sealed class SelectedRecipeBookletExportTests : IDisposable
{
    private static readonly string TestLogoSvgPath;
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RecipeDbContext> _options;
    private readonly string _testRoot;
    private readonly FakeWebHostEnvironment _env;
    private readonly RecipePdfModelFactory _factory;
    private readonly FakeTimeProvider _timeProvider;

    static SelectedRecipeBookletExportTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var tempDir = Path.Combine(Path.GetTempPath(), "BookletTests");
        Directory.CreateDirectory(tempDir);
        TestLogoSvgPath = Path.Combine(tempDir, "test-logo.svg");
        File.WriteAllText(TestLogoSvgPath,
            """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                <circle cx="50" cy="50" r="40" fill="#141414" />
            </svg>
            """);
    }

    public SelectedRecipeBookletExportTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new RecipeDbContext(_options);
        context.Database.EnsureCreated();

        _testRoot = Path.Combine(Path.GetTempPath(), $"BookletWebTests_{Guid.NewGuid():N}");
        var brandDir = Path.Combine(_testRoot, "images", "brand");
        Directory.CreateDirectory(brandDir);
        File.Copy(TestLogoSvgPath, Path.Combine(brandDir, "phe-la-logo.svg"), true);

        _env = new FakeWebHostEnvironment { WebRootPath = _testRoot };
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.FromHours(7)));

        var loader = new PdfMediaLoader(
            new SingleClientFactory(new HttpClient()),
            _env,
            NullLogger<PdfMediaLoader>.Instance);
        _factory = new RecipePdfModelFactory(loader, NullLogger<RecipePdfModelFactory>.Instance);
    }

    public void Dispose()
    {
        _connection.Dispose();
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private static RecipePdfModel CreateRecipe(string title, int stepCount = 2) => new()
    {
        Title = title,
        GeneralNote = "Ghi chú công thức",
        Ingredients =
        [
            new RecipePdfIngredientLine { Stt = 1, Name = "Trà Ô Long", Quantity = 100, Unit = "ml" }
        ],
        Steps = Enumerable.Range(1, stepCount).Select(i => new RecipePdfStepLine
        {
            StepNumber = i,
            Instruction = $"Thực hiện bước {i} cẩn thận."
        }).ToList()
    };

    [Fact]
    public void Booklet_has_cover_then_toc_then_recipe_sections()
    {
        var model = new RecipeBookletPdfModel(
            Recipes: [CreateRecipe("Món 1"), CreateRecipe("Món 2")],
            ExportedAt: DateTimeOffset.UtcNow,
            OfficialLogoSvgPath: TestLogoSvgPath);

        var doc = new RecipeBookletPdfDocument(model);
        var bytes = doc.GeneratePdf();

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.True(pdf.NumberOfPages >= 4);

        var page1 = pdf.GetPage(1);
        Assert.Contains("BỘ HƯỚNG DẪN", page1.Text);
        Assert.Contains("PHA CHẾ SẢN PHẨM", page1.Text);
        Assert.Contains("Phòng đào tạo - Phê La", page1.Text);

        var page2 = pdf.GetPage(2);
        Assert.Contains("MỤC LỤC", page2.Text);
        Assert.Contains("Món 1", page2.Text);
        Assert.Contains("Món 2", page2.Text);

        var page3 = pdf.GetPage(3);
        Assert.Contains("Món 1", page3.Text);

        var page4 = pdf.GetPage(4);
        Assert.Contains("Món 2", page4.Text);
    }

    [Fact]
    public void Booklet_toc_and_recipe_sections_preserve_model_order()
    {
        var model = new RecipeBookletPdfModel(
            Recipes: [CreateRecipe("Alpha (Id DESC cao)"), CreateRecipe("Beta (Id DESC thấp)")],
            ExportedAt: DateTimeOffset.UtcNow,
            OfficialLogoSvgPath: TestLogoSvgPath);

        var doc = new RecipeBookletPdfDocument(model);
        var bytes = doc.GeneratePdf();

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);

        Assert.Contains("Alpha", pdf.GetPage(3).Text);
        Assert.Contains("Beta", pdf.GetPage(4).Text);
    }

    [Fact]
    public void Booklet_metadata_contains_hub_author_count_and_injected_date_offset()
    {
        var date = new DateTimeOffset(2026, 10, 7, 14, 30, 0, TimeSpan.FromHours(7));
        var model = new RecipeBookletPdfModel(
            Recipes: [CreateRecipe("Trà sữa"), CreateRecipe("Cà phê trứng")],
            ExportedAt: date,
            OfficialLogoSvgPath: TestLogoSvgPath);

        var doc = new RecipeBookletPdfDocument(model);
        var metadata = doc.GetMetadata();

        Assert.Equal("Bộ hướng dẫn pha chế sản phẩm", metadata.Title);
        Assert.Equal("R&D Recipe Hub", metadata.Author);
        Assert.Equal("R&D Recipe Hub", metadata.Creator);
        Assert.Equal("2 công thức", metadata.Subject);
        Assert.Equal(date.DateTime, metadata.CreationDate);
    }

    [Fact]
    public void Booklet_with_fifty_max_length_titles_paginates_toc_without_losing_titles()
    {
        var recipes = Enumerable.Range(1, 50).Select(i =>
            CreateRecipe($"Công thức thứ {i:00} với tiêu đề rất dài để kiểm tra mục lục tự ngắt trang")).ToList();

        var model = new RecipeBookletPdfModel(
            Recipes: recipes,
            ExportedAt: DateTimeOffset.UtcNow,
            OfficialLogoSvgPath: TestLogoSvgPath);

        var doc = new RecipeBookletPdfDocument(model);
        var bytes = doc.GeneratePdf();

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.True(pdf.NumberOfPages >= 54);

        var allText = string.Join(" ", Enumerable.Range(1, pdf.NumberOfPages).Select(p => pdf.GetPage(p).Text));
        for (int i = 1; i <= 50; i++)
        {
            Assert.Contains($"Công thức thứ {i:00}", allText);
        }
    }

    [Fact]
    public void Missing_or_unreadable_logo_fails_with_configuration_error()
    {
        var model = new RecipeBookletPdfModel(
            Recipes: [CreateRecipe("Món")],
            ExportedAt: DateTimeOffset.UtcNow,
            OfficialLogoSvgPath: "non_existent_logo.svg");

        var ex = Assert.Throws<InvalidOperationException>(() => new RecipeBookletPdfDocument(model));
        Assert.Contains("Không tìm thấy asset logo chính thức", ex.Message);
    }

    [Fact]
    public void Recipe_section_with_missing_images_still_generates()
    {
        var recipe = CreateRecipe("Món không ảnh");
        recipe.HeroImageFullPath = null;
        recipe.Steps[0].ImageFullPath = null;

        var model = new RecipeBookletPdfModel(
            Recipes: [recipe],
            ExportedAt: DateTimeOffset.UtcNow,
            OfficialLogoSvgPath: TestLogoSvgPath);

        var doc = new RecipeBookletPdfDocument(model);
        var bytes = doc.GeneratePdf();

        Assert.NotNull(bytes);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
    }

    private async Task SeedRecipeAsync(RecipeDbContext db, int id, string name, bool ready = true)
    {
        var ing = await db.Ingredients.FirstOrDefaultAsync(i => i.Name == "Cốt trà");
        if (ing == null)
        {
            ing = new Ingredient { Name = "Cốt trà", NormalizedName = Ingredient.Normalize("Cốt trà"), DefaultUnit = "ml" };
            db.Ingredients.Add(ing);
            await db.SaveChangesAsync();
        }

        var recipe = new Recipe
        {
            Id = id,
            Name = name,
            GeneralNote = "Ghi chú",
            Ingredients = ready
                ? [new RecipeIngredient { Ingredient = ing, Quantity = 10 }]
                : [],
            Steps = ready
                ? [new RecipeStep { SortOrder = 1, Instruction = "Khuấy đều" }]
                : []
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(0, "Dữ liệu lựa chọn công thức không hợp lệ.")]
    [InlineData(-5, "Dữ liệu lựa chọn công thức không hợp lệ.")]
    public async Task Validation_invalid_or_non_positive_ids_rejects_with_400(int invalidId, string expectedMessage)
    {
        await using var db = new RecipeDbContext(_options);
        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        page.SelectedRecipeIds = [invalidId];

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, page.Response.StatusCode);
        Assert.Equal(expectedMessage, page.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Validation_empty_list_rejects_with_400()
    {
        await using var db = new RecipeDbContext(_options);
        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        page.SelectedRecipeIds = [];

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, page.Response.StatusCode);
        Assert.Equal("Hãy chọn ít nhất 1 công thức để xuất PDF.", page.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Validation_over_50_items_rejects_with_400()
    {
        await using var db = new RecipeDbContext(_options);
        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        page.SelectedRecipeIds = Enumerable.Range(1, 51).ToList();

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, page.Response.StatusCode);
        Assert.Equal("Chỉ có thể xuất tối đa 50 công thức mỗi lần.", page.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Validation_duplicate_ids_rejects_with_400()
    {
        await using var db = new RecipeDbContext(_options);
        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        page.SelectedRecipeIds = [1, 2, 1];

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, page.Response.StatusCode);
        Assert.Equal("Danh sách công thức có ID trùng lặp. Vui lòng chọn lại.", page.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Validation_missing_ids_rejects_with_400_listing_ids_descending()
    {
        await using var db = new RecipeDbContext(_options);
        await SeedRecipeAsync(db, 10, "Món 10", ready: true);

        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        page.SelectedRecipeIds = [10, 99, 44]; // 99 and 44 are missing

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, page.Response.StatusCode);
        Assert.Equal("Không tìm thấy công thức đã chọn: 99, 44.", page.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Validation_not_ready_recipes_rejects_with_400_listing_details_descending()
    {
        await using var db = new RecipeDbContext(_options);
        await SeedRecipeAsync(db, 20, "Món Sẵn Sàng", ready: true);
        await SeedRecipeAsync(db, 15, "Món Thiếu Bước", ready: false);
        await SeedRecipeAsync(db, 5, "Món Thiếu NL", ready: false);

        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        page.SelectedRecipeIds = [20, 15, 5];

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, page.Response.StatusCode);
        Assert.Equal("Công thức chưa đủ điều kiện xuất PDF: Món Thiếu Bước (#15), Món Thiếu NL (#5)", page.ModelState[string.Empty]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Success_export_orders_by_id_descending_and_returns_correct_filename()
    {
        await using var db = new RecipeDbContext(_options);
        await SeedRecipeAsync(db, 2, "Trà Đào (Id 2)", ready: true);
        await SeedRecipeAsync(db, 8, "Trà Vải (Id 8)", ready: true);
        await SeedRecipeAsync(db, 5, "Trà Sen (Id 5)", ready: true);

        var page = new IndexModel(db, new FakeImageStorageService(), _factory, _env, _timeProvider);
        page.PageContext = new PageContext { HttpContext = new DefaultHttpContext() };
        // Pass ascending order deliberately
        page.SelectedRecipeIds = [2, 5, 8];

        var result = await page.OnPostExportSelectedAsync(CancellationToken.None);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.Equal("recipe-collection-20261007-1000.pdf", fileResult.FileDownloadName);

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(fileResult.FileContents);
        // Order in PDF recipe pages must be Id 8, then Id 5, then Id 2
        Assert.Contains("Trà Vải (Id 8)", pdf.GetPage(3).Text);
        Assert.Contains("Trà Sen (Id 5)", pdf.GetPage(4).Text);
        Assert.Contains("Trà Đào (Id 2)", pdf.GetPage(5).Text);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeImageStorageService : IImageStorageService
    {
        public Task<StoredImage> UploadAsync(Stream image, ValidatedImageInfo info, CancellationToken cancellationToken = default)
            => Task.FromResult(new StoredImage("id", "url", 100, "image/jpeg"));
        public Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) => Task.FromResult("file.jpg");
        public void DeleteFile(string? fileName) { }
    }

    private sealed class SpyRecipePdfModelFactory : IRecipePdfModelFactory
    {
        public int CallCount { get; private set; }

        public Task<RecipePdfModelBatch> CreateAsync(IReadOnlyList<Recipe> recipes, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new RecipePdfModelBatch([], null));
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }
}
