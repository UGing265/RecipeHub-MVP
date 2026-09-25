# Phase 3 — Recipe composer

## Context

- Parent: [plan.md](./plan.md)
- Depends on: [phase-02-ingredient-catalog.md](./phase-02-ingredient-catalog.md)
- Reference: `doc/PRN232.SRS.md:226-248`

## Overview

Build the recipe composer where users name a recipe, attach ingredients with positive quantities, and author ordered instruction steps with optional photos.

## Requirements

- Recipe requires a non-empty name.
- Ingredients are chosen from the catalog. Each ingredient can appear at most once in a recipe.
- Quantities must be greater than zero. The unit is read-only, matching the catalog ingredient.
- Steps are strictly ordered starting at 1. Each step requires non-empty instruction text.
- Each step can optionally have one uploaded image.
- Image uploads are restricted to `.jpg`, `.jpeg`, `.png`, and `.webp`, with a 5 MiB maximum size.
- Uploaded files are stored with random GUID names under `wwwroot/uploads/steps/`. The database stores the file name only.
- Users can move steps up or down.
- Deleting a recipe deletes its lines, steps, and associated step image files from disk.

## Files

| Action | Path | Responsibility |
|---|---|---|
| Create | `src/RecipeCard.Web/Pages/Recipes/Index.cshtml` | Recipe list with edit, preview, and delete actions. |
| Create | `src/RecipeCard.Web/Pages/Recipes/Index.cshtml.cs` | Load recipes with line/step counts; handle delete. |
| Create | `src/RecipeCard.Web/Pages/Recipes/Create.cshtml` | Quick create form (Name, GeneralNote). |
| Create | `src/RecipeCard.Web/Pages/Recipes/Create.cshtml.cs` | Validate name; insert draft recipe; redirect to edit. |
| Create | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml` | Unified composer: basic info, ingredient list editor, step list editor with reorder and photo upload. |
| Create | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs` | Handles basic info updates, adding/removing ingredient lines, adding/removing/moving steps, and file uploads. |
| Create | `src/RecipeCard.Web/Services/ImageStorageService.cs` | Validate extension and magic header, save file under GUID name, delete physical files. |
| Create | `tests/RecipeCard.Web.Tests/RecipeComposerTests.cs` | Tests for duplicate ingredient rejection, non-positive quantity rejection, step ordering, and image extension safety. |

## Implementation Steps

1. Create `ImageStorageService`:
   - Enforce allowed extensions: `.jpg`, `.jpeg`, `.png`, `.webp`.
   - Check length `> 0` and `<= 5 * 1024 * 1024`.
   - Read first bytes to verify known magic headers (avoid trusting MIME types alone).
   - Write file to `Path.Combine(_environment.WebRootPath, "uploads", "steps", $"{Guid.NewGuid():N}{ext}")`.
   - Provide `DeleteFile(string? fileName)` that ignores nulls and deletes from disk safely.

2. Build `Edit.cshtml.cs` page handlers:
   - `OnGetAsync(int id)`: load recipe including `Ingredients` (then `Ingredient`) and `Steps` ordered by `SortOrder`. Load available ingredients for the dropdown.
   - `OnPostSaveDetailsAsync(int id, ...)`: update name and general note.
   - `OnPostAddIngredientAsync(int id, int ingredientId, decimal quantity)`:
     - Reject if `quantity <= 0`.
     - Reject if ingredient already exists in recipe.
     - Add line and redirect back to `Edit`.
   - `OnPostRemoveIngredientAsync(int id, int recipeIngredientId)`: remove line and save.
   - `OnPostAddStepAsync(int id, string instruction, IFormFile? image)`:
     - Reject empty instruction.
     - Next `SortOrder = (await _db.RecipeSteps.Where(s => s.RecipeId == id).MaxAsync(s => (int?)s.SortOrder) ?? 0) + 1`.
     - If file provided, process through `ImageStorageService` to get saved file name.
     - Wrap database addition in `try / catch`:
       ```csharp
       string? savedFileName = null;
       if (image != null) savedFileName = await _imageStorage.SaveAsync(image);
       try
       {
           var step = new RecipeStep { RecipeId = id, Instruction = instruction, SortOrder = nextOrder, ImageFileName = savedFileName };
           _db.RecipeSteps.Add(step);
           await _db.SaveChangesAsync();
       }
       catch
       {
           if (savedFileName != null) _imageStorage.DeleteFile(savedFileName); // Prevent orphan file on disk
           throw;
       }
       ```
   - `OnPostMoveStepAsync(int id, int stepId, string direction)`:
     - Load all steps for recipe ordered by `SortOrder`.
     - Find index of target step; calculate adjacent index (up: -1, down: +1). If out of bounds, do nothing.
     - To prevent SQLite unique index collisions during swap, execute in a transaction using a two-pass assignment:
       - Pass 1: set all steps' `SortOrder` to temporary negative numbers (`-1, -2, ...`).
       - Swap targets in the in-memory collection.
       - Pass 2: assign clean sequential `SortOrder` from `1` to `N`.
       - Commit transaction.
   - `OnPostDeleteStepAsync(int id, int stepId)`:
     - Delete image file if present.
     - Remove step.
     - Renumber remaining steps in memory and persist in one transaction to preserve continuous `1..N` ordering.
3. Build `Edit.cshtml` UI:
   - Section 1: Recipe details.
   - Section 2: Table of current ingredients (Name, Quantity, Unit, Remove button) + inline add form.
   - Section 3: List of steps with step number, instruction, photo preview if present, file picker, Move Up / Move Down buttons, and Delete button.
   - Section 4: "Xem trước & Xuất PDF" button pointing to preview page.

4. Add composer tests:
   - Quantity `0` or `-5` fails validation; line is not added.
   - Adding an ingredient already in the recipe fails validation.
   - Step upload of a `.txt` or `.exe` file is rejected.
   - Moving step 2 up makes it step 1; prior step 1 becomes step 2 without SQLite unique constraint error.
   - Image file is deleted from disk if database save throws an exception during step creation.

## Success Criteria

- User can compose a complete recipe with ingredients, units, quantities, and sequential instructions.
- Uploaded valid images display on the composer page next to their step.
- Step order swaps predictably and renumbers cleanly on deletions.
- Deleting the recipe purges both records and physical image files from disk.
- All composer tests pass.

## Todo

- [ ] Create `ImageStorageService` with size and type checks.
- [ ] Implement Recipe Index, Create, and Edit Razor Pages.
- [ ] Implement ingredient line adding/removal with quantity validation.
- [ ] Implement step addition, deletion, reordering, and photo upload.
- [ ] Add and pass `RecipeComposerTests`.
