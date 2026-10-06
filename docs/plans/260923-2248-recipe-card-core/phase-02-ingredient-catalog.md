# Phase 2 — Ingredient catalog

## Context

- Parent: [plan.md](./plan.md)
- Depends on: [phase-01-bootstrap-and-persistence.md](./phase-01-bootstrap-and-persistence.md)
- Reference: `docs/materials/PRN232.SRS.md:214-223`

## Overview

Build the ingredient catalog as a standalone Razor Pages feature. Users create, view, edit, and delete ingredients. An ingredient referenced by any recipe cannot be deleted.

## Requirements

- Ingredients require a non-empty name and non-empty default unit of measure.
- Ingredient names are unique (case-insensitive).
- A duplicate name adds a model-state error and redisplays the form with the user's input.
- Deleting an ingredient referenced by one or more recipes is blocked; the UI displays a readable error without an unhandled database exception.
- No inventory, stock quantity, cost, supplier, or category is supported.

## Files

| Action | Path | Responsibility |
|---|---|---|
| Create | `src/RecipeCard.Web/Pages/Ingredients/Index.cshtml` | Ingredient list with table, Add button, Edit links, and Delete forms. |
| Create | `src/RecipeCard.Web/Pages/Ingredients/Index.cshtml.cs` | Load ingredients; handle delete POST; check recipe references first. |
| Create | `src/RecipeCard.Web/Pages/Ingredients/Create.cshtml` | Creation form. |
| Create | `src/RecipeCard.Web/Pages/Ingredients/Create.cshtml.cs` | Validate and save new ingredient. |
| Create | `src/RecipeCard.Web/Pages/Ingredients/Edit.cshtml` | Edit form. |
| Create | `src/RecipeCard.Web/Pages/Ingredients/Edit.cshtml.cs` | Validate and update ingredient. |
| Modify | `src/RecipeCard.Web/Pages/Shared/_Layout.cshtml` | Add top navigation link for Ingredients. |
| Create | `tests/RecipeCard.Web.Tests/IngredientPageTests.cs` | Tests for duplicate-name rejection and delete protection. |

## Implementation Steps

1. Create input and display view models with validation attributes:

   ```csharp
   public sealed class IngredientInputModel
   {
       [Required(ErrorMessage = "Tên nguyên liệu không được để trống")]
       [StringLength(100, ErrorMessage = "Tên không dài quá 100 ký tự")]
       public string Name { get; set; } = string.Empty;

       [Required(ErrorMessage = "Đơn vị không được để trống")]
       [StringLength(20, ErrorMessage = "Đơn vị không dài quá 20 ký tự")]
       public string DefaultUnit { get; set; } = string.Empty;
   }
   ```

2. Implement `Create.cshtml.cs`:
   - Trim inputs before checks.
   - Pre-check: `RecipeDbContext.Ingredients.AnyAsync(i => EF.Functions.Collate(i.Name, "NOCASE") == trimmedName)`.
   - Add model error `"Name"`: `"Nguyên liệu này đã tồn tại."` if duplicate.
   - Wrap `await _db.SaveChangesAsync()` in `try / catch (DbUpdateException)` to catch database unique constraint races; if caught, add model error `"Name"`: `"Nguyên liệu này đã tồn tại."` and return `Page()`.
   - On success, redirect to `./Index`.

3. Implement `Edit.cshtml.cs`:
   - Check duplicate name against other ingredients: `i.Id != currentId`.
   - Wrap `SaveChangesAsync()` in `try / catch (DbUpdateException)` for unique constraint races.
   - Save and redirect to `./Index`.

4. Implement `Index.cshtml.cs`:
   - Load ingredients ordered by name.
   - `OnPostDeleteAsync(int id)`:
     - Pre-check: `var isUsed = await _db.RecipeIngredients.AnyAsync(ri => ri.IngredientId == id);`
     - If used, set `ErrorMessage = "Không thể xóa nguyên liệu đang có trong công thức."` and return `await OnGetAsync()`.
     - If not used, mark for removal and wrap `await _db.SaveChangesAsync()` in `try / catch (DbUpdateException)` to catch foreign key restrict races if a reference was added concurrently; catch maps to the same friendly `ErrorMessage` without unhandled crash.
5. Add tests in `IngredientPageTests.cs`:
   - Submitting a duplicate name rejects the request with a validation error and leaves database count at 1.
   - Concurrent or database-level duplicate violation during save is caught and redisplays form with error.
   - Attempting to delete a referenced ingredient does not delete it and sets `ErrorMessage`.
   - Database-level foreign key violation during delete is caught and sets `ErrorMessage`.
   - Deleting an unreferenced ingredient succeeds.

## Success Criteria

- User can add ingredients from the browser and immediately see them in the list.
- Submitting an empty name or unit is rejected server-side.
- Adding "Bột sữa" when "bột sữa" exists is rejected with a clear message.
- Deleting an ingredient used in a recipe fails gracefully and preserves the recipe.
- All new tests pass.

## Todo

- [ ] Implement Ingredient Razor Pages (Index, Create, Edit).
- [ ] Add navigation entry in `_Layout.cshtml`.
- [ ] Wire duplicate name and delete-protection checks.
- [ ] Add and pass `IngredientPageTests`.
