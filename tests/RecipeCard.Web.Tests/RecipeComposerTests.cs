using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Recipes;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Tests;

public sealed class RecipeComposerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RecipeDbContext> _options;
    private readonly string _testRoot;
    private readonly FakeWebHostEnvironment _env;
    private readonly ImageStorageService _imageStorage;

    public RecipeComposerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new RecipeDbContext(_options);
        context.Database.EnsureCreated();

        _testRoot = Path.Combine(Path.GetTempPath(), $"RecipeCardTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_testRoot, "uploads", "steps"));

        _env = new FakeWebHostEnvironment { WebRootPath = _testRoot };
        _imageStorage = new ImageStorageService(_env);
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
    public async Task Add_ingredient_with_zero_or_negative_quantity_is_rejected()
    {
        await using var db = new RecipeDbContext(_options);
        var ing = new Ingredient { Name = "Đường", NormalizedName = Ingredient.Normalize("Đường"), DefaultUnit = "g" };
        var recipe = new Recipe { Name = "Trà Oolong" };
        db.Ingredients.Add(ing);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new EditModel(db, _imageStorage);
        var result = await page.OnPostAddIngredientAsync(recipe.Id, ing.Id, 0);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Định lượng nguyên liệu phải lớn hơn 0.", page.ErrorMessage);
        Assert.Empty(await db.RecipeIngredients.ToListAsync());
    }

    [Fact]
    public async Task Add_duplicate_ingredient_to_recipe_is_rejected()
    {
        await using var db = new RecipeDbContext(_options);
        var ing = new Ingredient { Name = "Sữa đặc", NormalizedName = Ingredient.Normalize("Sữa đặc"), DefaultUnit = "g" };
        var recipe = new Recipe { Name = "Phê Nâu" };
        db.Ingredients.Add(ing);
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new EditModel(db, _imageStorage);
        await page.OnPostAddIngredientAsync(recipe.Id, ing.Id, 30);

        // Try adding the same ingredient again
        var result = await page.OnPostAddIngredientAsync(recipe.Id, ing.Id, 40);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Nguyên liệu này đã có trong công thức. Vui lòng chọn nguyên liệu khác.", page.ErrorMessage);
        Assert.Single(await db.RecipeIngredients.ToListAsync());
    }

    [Fact]
    public async Task Add_step_saves_with_sequential_sort_order()
    {
        await using var db = new RecipeDbContext(_options);
        var recipe = new Recipe { Name = "Trà sữa" };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new EditModel(db, _imageStorage);
        await page.OnPostAddStepAsync(recipe.Id, "Bước đầu tiên", null);
        await page.OnPostAddStepAsync(recipe.Id, "Bước thứ hai", null);

        var steps = await db.RecipeSteps.Where(s => s.RecipeId == recipe.Id).OrderBy(s => s.SortOrder).ToListAsync();
        Assert.Equal(2, steps.Count);
        Assert.Equal(1, steps[0].SortOrder);
        Assert.Equal("Bước đầu tiên", steps[0].Instruction);
        Assert.Equal(2, steps[1].SortOrder);
        Assert.Equal("Bước thứ hai", steps[1].Instruction);
    }

    [Fact]
    public async Task Move_step_swaps_sort_orders_cleanly_without_collision()
    {
        await using var db = new RecipeDbContext(_options);
        var recipe = new Recipe
        {
            Name = "Món thử",
            Steps =
            [
                new RecipeStep { SortOrder = 1, Instruction = "A" },
                new RecipeStep { SortOrder = 2, Instruction = "B" }
            ]
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var step2 = recipe.Steps.Single(s => s.Instruction == "B");

        var page = new EditModel(db, _imageStorage);
        var result = await page.OnPostMoveStepAsync(recipe.Id, step2.Id, "up");

        Assert.IsType<RedirectToPageResult>(result);

        var steps = await db.RecipeSteps.Where(s => s.RecipeId == recipe.Id).OrderBy(s => s.SortOrder).ToListAsync();
        Assert.Equal("B", steps[0].Instruction);
        Assert.Equal(1, steps[0].SortOrder);
        Assert.Equal("A", steps[1].Instruction);
        Assert.Equal(2, steps[1].SortOrder);
    }

    [Fact]
    public async Task Delete_step_deletes_image_and_renumbers_remaining()
    {
        await using var db = new RecipeDbContext(_options);
        var fakeJpg = CreateFakeImageFile("step.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
        var savedImage = await _imageStorage.SaveAsync(fakeJpg);
        var diskPath = Path.Combine(_testRoot, "uploads", "steps", savedImage);
        Assert.True(File.Exists(diskPath));

        var recipe = new Recipe
        {
            Name = "Món có 3 bước",
            Steps =
            [
                new RecipeStep { SortOrder = 1, Instruction = "Bước 1" },
                new RecipeStep { SortOrder = 2, Instruction = "Bước 2", ImageFileName = savedImage },
                new RecipeStep { SortOrder = 3, Instruction = "Bước 3" }
            ]
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var step2 = recipe.Steps.Single(s => s.SortOrder == 2);

        var page = new EditModel(db, _imageStorage);
        await page.OnPostDeleteStepAsync(recipe.Id, step2.Id);

        // Step 2 image must be deleted from disk
        Assert.False(File.Exists(diskPath));

        // Remaining steps must be renumbered 1 and 2
        var remaining = await db.RecipeSteps.Where(s => s.RecipeId == recipe.Id).OrderBy(s => s.SortOrder).ToListAsync();
        Assert.Equal(2, remaining.Count);
        Assert.Equal(1, remaining[0].SortOrder);
        Assert.Equal("Bước 1", remaining[0].Instruction);
        Assert.Equal(2, remaining[1].SortOrder);
        Assert.Equal("Bước 3", remaining[1].Instruction);
    }

    [Fact]
    public async Task ImageStorage_rejects_invalid_extension_or_header()
    {
        // 1. Text file with .txt extension
        var txtFile = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("not an image")), 0, 13, "test", "doc.txt");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _imageStorage.SaveAsync(txtFile));

        // 2. Text content with .jpg extension (header mismatch)
        var fakeExtFile = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("fake content")), 0, 12, "test", "fake.jpg");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _imageStorage.SaveAsync(fakeExtFile));
    }

    [Fact]
    public async Task Delete_recipe_deletes_all_steps_and_images_on_disk()
    {
        await using var db = new RecipeDbContext(_options);
        var fakePng = CreateFakeImageFile("step.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var savedImage = await _imageStorage.SaveAsync(fakePng);
        var diskPath = Path.Combine(_testRoot, "uploads", "steps", savedImage);
        Assert.True(File.Exists(diskPath));

        var recipe = new Recipe
        {
            Name = "Xóa toàn bộ",
            Steps = [new RecipeStep { SortOrder = 1, Instruction = "A", ImageFileName = savedImage }]
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var indexPage = new IndexModel(
            db,
            _imageStorage,
            new RecipeCard.Web.Services.RecipePdfModelFactory(new RecipeCard.Web.Services.PdfMediaLoader(new TestHttpClientFactory(), _env, Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeCard.Web.Services.PdfMediaLoader>.Instance), Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeCard.Web.Services.RecipePdfModelFactory>.Instance),
            _env,
            TimeProvider.System);
        await indexPage.OnPostDeleteAsync(recipe.Id);

        Assert.Empty(await db.Recipes.ToListAsync());
        Assert.Empty(await db.RecipeSteps.ToListAsync());
        Assert.False(File.Exists(diskPath));
    }

    private static IFormFile CreateFakeImageFile(string filename, byte[] header)
    {
        var stream = new MemoryStream();
        stream.Write(header, 0, header.Length);
        var dummyBytes = new byte[64];
        stream.Write(dummyBytes, 0, dummyBytes.Length);
        stream.Position = 0;

        return new FormFile(stream, 0, stream.Length, "file", filename);
    }


    private sealed class TestHttpClientFactory : System.Net.Http.IHttpClientFactory
    {
        public System.Net.Http.HttpClient CreateClient(string name) => new();
    }
}
