# Phase 1 — Bootstrap and persistence

## Context

- Parent: [plan.md](./plan.md)
- Reference: `doc/PRN232.SRS.md:289-314`

## Overview

Create one .NET 10 Razor Pages project, install EF Core SQLite, model the four core entities, and apply an initial migration that creates `recipe-card.db` on disk.

## Requirements

- One solution containing exactly one web project (`RecipeCard.Web`) and one test project (`RecipeCard.Web.Tests`).
- No authentication, API project, service project, or repository layer.
- Foreign keys and check constraints preserve core integrity:
  - Deleting a recipe cascades to its ingredient lines and steps.
  - Deleting an ingredient referenced by any recipe is restricted (`DeleteBehavior.Restrict`).
  - `Ingredient.Name` has a unique index with SQLite `NOCASE` collation.
  - `RecipeIngredient.Quantity` enforces a database check constraint `Quantity > 0`.
  - `RecipeStep.SortOrder` is tracked sequentially per recipe.

## Files

| Action | Path | Responsibility |
|---|---|---|
| Create | `RecipeCard.sln` | Solution root. |
| Create | `src/RecipeCard.Web/RecipeCard.Web.csproj` | .NET 10 Razor Pages project and NuGet dependencies. |
| Modify | `src/RecipeCard.Web/Program.cs` | Register Razor Pages, `RecipeDbContext`, static files, and startup migration application. |
| Create | `src/RecipeCard.Web/appsettings.json` | `ConnectionStrings:RecipeDb` points to local `recipe-card.db`. |
| Create | `src/RecipeCard.Web/Data/RecipeDbContext.cs` | `DbSet`s, unique indexes, FK/delete behavior, decimal conversion. |
| Create | `src/RecipeCard.Web/Models/Ingredient.cs` | Ingredient entity. |
| Create | `src/RecipeCard.Web/Models/Recipe.cs` | Recipe aggregate root. |
| Create | `src/RecipeCard.Web/Models/RecipeIngredient.cs` | Recipe/ingredient quantity line. |
| Create | `src/RecipeCard.Web/Models/RecipeStep.cs` | Ordered instruction and optional image file name. |
| Create | `src/RecipeCard.Web/Migrations/*` | EF-generated `InitialCreate` migration. |
| Create | `tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj` | xUnit test project. |
| Create | `tests/RecipeCard.Web.Tests/RecipeDbContextTests.cs` | Mapping and relational-rule tests using SQLite temporary database. |

## Implementation Steps

1. Create solution and web project:

   ```powershell
   dotnet new sln --name RecipeCard
   dotnet new webapp --name RecipeCard.Web --output src/RecipeCard.Web --framework net10.0
   dotnet sln RecipeCard.sln add src/RecipeCard.Web/RecipeCard.Web.csproj
   ```

2. Add persistence and test dependencies:

   ```powershell
   dotnet add src/RecipeCard.Web package Microsoft.EntityFrameworkCore.Sqlite
   dotnet add src/RecipeCard.Web package Microsoft.EntityFrameworkCore.Design
   dotnet new xunit --name RecipeCard.Web.Tests --output tests/RecipeCard.Web.Tests --framework net10.0
   dotnet add tests/RecipeCard.Web.Tests reference src/RecipeCard.Web
   dotnet sln RecipeCard.sln add tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj
   dotnet tool install --global dotnet-ef
   ```

3. Define entities exactly:

   ```csharp
   public sealed class Ingredient
   {
       public int Id { get; set; }
       public string Name { get; set; } = string.Empty;
       public string DefaultUnit { get; set; } = string.Empty;
       public List<RecipeIngredient> RecipeIngredients { get; set; } = [];
   }

   public sealed class Recipe
   {
       public int Id { get; set; }
       public string Name { get; set; } = string.Empty;
       public string? GeneralNote { get; set; }
       public List<RecipeIngredient> Ingredients { get; set; } = [];
       public List<RecipeStep> Steps { get; set; } = [];
   }
   ```

   `RecipeIngredient` contains `IngredientId`, `RecipeId`, and `decimal Quantity`. `RecipeStep` contains `RecipeId`, `int SortOrder`, `string Instruction`, and nullable `string ImageFileName`.

4. Configure the context:

   ```csharp
   builder.Services.AddDbContext<RecipeDbContext>(options =>
       options.UseSqlite(builder.Configuration.GetConnectionString("RecipeDb")));
   ```

   Configure database constraints explicitly in `OnModelCreating`:
   - `Ingredient.Name`: `b.Property(i => i.Name).UseCollation("NOCASE"); b.HasIndex(i => i.Name).IsUnique();`
   - `RecipeIngredient`: `b.HasIndex(ri => new { ri.RecipeId, ri.IngredientId }).IsUnique();`
   - `RecipeIngredient.Quantity`: `b.ToTable(t => t.HasCheckConstraint("CK_RecipeIngredient_Quantity_Positive", "Quantity > 0"));`
   - Foreign keys: `Recipe → RecipeIngredients` (Cascade), `Recipe → Steps` (Cascade), `RecipeIngredient → Ingredient` (Restrict).
5. Add the initial migration and create the SQLite file:

   ```powershell
   dotnet ef migrations add InitialCreate --project src/RecipeCard.Web --startup-project src/RecipeCard.Web
   dotnet ef database update --project src/RecipeCard.Web --startup-project src/RecipeCard.Web
   ```

6. Add tests before treating the schema as complete:

   ```csharp
   [Fact]
   public async Task Deleting_a_recipe_cascades_its_lines_and_steps() { /* arrange recipe with lines/steps; delete; assert cascaded */ }

   [Fact]
   public async Task Referenced_ingredient_cannot_be_deleted() { /* arrange reference; attempt delete; assert DbUpdateException */ }

   [Fact]
   public async Task Recipe_cannot_contain_duplicate_ingredient() { /* arrange duplicate line; assert DbUpdateException */ }

   [Fact]
   public async Task Quantity_less_than_or_equal_to_zero_is_rejected_by_database() { /* arrange quantity <= 0; assert DbUpdateException */ }

   [Fact]
   public async Task Duplicate_ingredient_name_with_different_casing_is_rejected_by_database() { /* insert "Bột sữa", then "bột SỮA"; assert DbUpdateException */ }

   Use an open temporary SQLite connection, not EF Core's in-memory provider, so indexes and foreign keys are exercised.

## Success Criteria

- `dotnet build RecipeCard.sln` succeeds.
- `dotnet test RecipeCard.sln` passes.
- `src/RecipeCard.Web/recipe-card.db` exists after database update and contains all four tables.
- Schema tests prove cascade, restrict, and unique-index behavior.

## Risks and Controls

| Risk | Control |
|---|---|
| `EnsureCreated` bypasses migration history | Use the initial migration from the start. |
| SQLite foreign-key behavior differs from test doubles | Tests use a real SQLite connection. |
| Decimal precision differs by provider | Quantities are validated and rendered as entered; no arithmetic/scaling is in scope. |

## Todo

- [ ] Create solution, project, packages, and test project.
- [ ] Implement entities and `RecipeDbContext`.
- [ ] Generate/apply initial migration.
- [ ] Prove relational rules with SQLite tests.
