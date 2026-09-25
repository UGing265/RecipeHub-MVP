using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Tests;

public sealed class RecipeDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RecipeDbContext> _options;

    public RecipeDbContextTests()
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
    public async Task Deleting_a_recipe_cascades_its_lines_and_steps()
    {
        await using var context = new RecipeDbContext(_options);
        var ingredient = new Ingredient
        {
            Name = "Cà phê phin",
            NormalizedName = Ingredient.Normalize("Cà phê phin"),
            DefaultUnit = "ml"
        };
        var recipe = new Recipe
        {
            Name = "Phê Đen",
            Ingredients =
            [
                new RecipeIngredient { Ingredient = ingredient, Quantity = 70 }
            ],
            Steps =
            [
                new RecipeStep { SortOrder = 1, Instruction = "Đong cà phê" }
            ]
        };

        context.Recipes.Add(recipe);
        await context.SaveChangesAsync();

        context.Recipes.Remove(recipe);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Recipes.ToListAsync());
        Assert.Empty(await context.RecipeIngredients.ToListAsync());
        Assert.Empty(await context.RecipeSteps.ToListAsync());
        Assert.Single(await context.Ingredients.ToListAsync());
    }

    [Fact]
    public async Task Referenced_ingredient_cannot_be_deleted()
    {
        int ingredientId;
        await using (var context = new RecipeDbContext(_options))
        {
            var ingredient = new Ingredient
            {
                Name = "Sữa đặc",
                NormalizedName = Ingredient.Normalize("Sữa đặc"),
                DefaultUnit = "g"
            };
            var recipe = new Recipe
            {
                Name = "Phê Nâu",
                Ingredients =
                [
                    new RecipeIngredient { Ingredient = ingredient, Quantity = 35 }
                ],
                Steps =
                [
                    new RecipeStep { SortOrder = 1, Instruction = "Khuấy sữa" }
                ]
            };

            context.Recipes.Add(recipe);
            await context.SaveChangesAsync();
            ingredientId = ingredient.Id;
        }

        await using (var deleteContext = new RecipeDbContext(_options))
        {
            var toDelete = await deleteContext.Ingredients.FindAsync(ingredientId);
            Assert.NotNull(toDelete);
            deleteContext.Ingredients.Remove(toDelete);
            await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Recipe_cannot_contain_duplicate_ingredient()
    {
        await using var context = new RecipeDbContext(_options);
        var ingredient = new Ingredient
        {
            Name = "Đường nước",
            NormalizedName = Ingredient.Normalize("Đường nước"),
            DefaultUnit = "g"
        };
        var recipe = new Recipe
        {
            Name = "Trà ngọt",
            Ingredients =
            [
                new RecipeIngredient { Ingredient = ingredient, Quantity = 10 },
                new RecipeIngredient { Ingredient = ingredient, Quantity = 20 }
            ]
        };

        context.Recipes.Add(recipe);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Quantity_less_than_or_equal_to_zero_is_rejected_by_database()
    {
        await using var context = new RecipeDbContext(_options);
        var ingredient = new Ingredient
        {
            Name = "Đá viên",
            NormalizedName = Ingredient.Normalize("Đá viên"),
            DefaultUnit = "g"
        };
        var recipe = new Recipe
        {
            Name = "Cà phê đá",
            Ingredients =
            [
                new RecipeIngredient { Ingredient = ingredient, Quantity = 0 }
            ]
        };

        context.Recipes.Add(recipe);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_ingredient_name_with_different_casing_is_rejected_by_database()
    {
        await using var context = new RecipeDbContext(_options);
        var ingredient1 = new Ingredient
        {
            Name = "Bột sữa",
            NormalizedName = Ingredient.Normalize("Bột sữa"),
            DefaultUnit = "g"
        };
        var ingredient2 = new Ingredient
        {
            Name = "bột SỮA",
            NormalizedName = Ingredient.Normalize("bột SỮA"),
            DefaultUnit = "g"
        };

        context.Ingredients.Add(ingredient1);
        await context.SaveChangesAsync();

        context.Ingredients.Add(ingredient2);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
