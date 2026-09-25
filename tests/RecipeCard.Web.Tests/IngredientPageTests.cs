using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Ingredients;

namespace RecipeCard.Web.Tests;

public sealed class IngredientPageTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RecipeDbContext> _options;

    public IngredientPageTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();

        _options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new RecipeDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task Create_valid_ingredient_saves_trimmed_and_normalized()
    {
        await using var db = new RecipeDbContext(_options);
        var page = new CreateModel(db)
        {
            Input = new CreateModel.InputModel
            {
                Name = "  Bột sữa nguyên kem  ",
                DefaultUnit = "  g  "
            }
        };

        var result = await page.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Index", redirect.PageName);

        var saved = await db.Ingredients.SingleAsync();
        Assert.Equal("Bột sữa nguyên kem", saved.Name);
        Assert.Equal("g", saved.DefaultUnit);
        Assert.Equal(Ingredient.Normalize("Bột sữa nguyên kem"), saved.NormalizedName);
    }

    [Fact]
    public async Task Create_duplicate_name_with_different_casing_adds_model_error()
    {
        await using var db = new RecipeDbContext(_options);
        db.Ingredients.Add(new Ingredient
        {
            Name = "Sữa đặc",
            NormalizedName = Ingredient.Normalize("Sữa đặc"),
            DefaultUnit = "g"
        });
        await db.SaveChangesAsync();

        var page = new CreateModel(db)
        {
            Input = new CreateModel.InputModel
            {
                Name = "sữa ĐẶC",
                DefaultUnit = "g"
            }
        };

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.ContainsKey("Input.Name"));
        Assert.Single(await db.Ingredients.ToListAsync());
    }

    [Fact]
    public async Task Create_whitespace_only_name_fails_validation()
    {
        await using var db = new RecipeDbContext(_options);
        var page = new CreateModel(db)
        {
            Input = new CreateModel.InputModel
            {
                Name = "   ",
                DefaultUnit = "g"
            }
        };

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.ContainsKey("Input.Name"));
        Assert.Empty(await db.Ingredients.ToListAsync());
    }

    [Fact]
    public async Task Edit_duplicate_name_rejects_and_preserves_original()
    {
        await using var db = new RecipeDbContext(_options);
        var ing1 = new Ingredient { Name = "Đường cát", NormalizedName = Ingredient.Normalize("Đường cát"), DefaultUnit = "g" };
        var ing2 = new Ingredient { Name = "Đường nước", NormalizedName = Ingredient.Normalize("Đường nước"), DefaultUnit = "ml" };
        db.Ingredients.AddRange(ing1, ing2);
        await db.SaveChangesAsync();

        var page = new EditModel(db)
        {
            Input = new EditModel.InputModel
            {
                Id = ing2.Id,
                Name = "đường CÁT",
                DefaultUnit = "ml"
            }
        };

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(page.ModelState.ContainsKey("Input.Name"));

        var freshIng2 = await db.Ingredients.FindAsync(ing2.Id);
        Assert.NotNull(freshIng2);
        Assert.Equal("Đường nước", freshIng2.Name);
    }

    [Fact]
    public async Task Delete_unreferenced_ingredient_succeeds()
    {
        await using var db = new RecipeDbContext(_options);
        var ing = new Ingredient { Name = "Muối biển", NormalizedName = Ingredient.Normalize("Muối biển"), DefaultUnit = "g" };
        db.Ingredients.Add(ing);
        await db.SaveChangesAsync();

        var page = new IndexModel(db);
        var result = await page.OnPostDeleteAsync(ing.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Empty(await db.Ingredients.ToListAsync());
        Assert.Equal("Đã xóa nguyên liệu thành công.", page.SuccessMessage);
    }

    [Fact]
    public async Task Delete_referenced_ingredient_is_blocked_and_sets_error_message()
    {
        await using var db = new RecipeDbContext(_options);
        var ing = new Ingredient { Name = "Trà Oolong", NormalizedName = Ingredient.Normalize("Trà Oolong"), DefaultUnit = "ml" };
        var recipe = new Recipe
        {
            Name = "Trà Oolong Sữa",
            Ingredients = [new RecipeIngredient { Ingredient = ing, Quantity = 100 }],
            Steps = [new RecipeStep { SortOrder = 1, Instruction = "Rót trà" }]
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var page = new IndexModel(db);
        var result = await page.OnPostDeleteAsync(ing.Id);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Single(await db.Ingredients.ToListAsync());
        Assert.Equal("Không thể xóa nguyên liệu đang có trong công thức.", page.ErrorMessage);
    }
}
