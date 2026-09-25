# Technical Journal: Recipe Card Core Implementation Completed

- **Date:** 2026-09-23 23:15
- **Status:** Completed
- **Project:** R&D Recipe Hub (Core Slice)
- **Framework:** .NET 10 (ASP.NET Core Razor Pages)

## What Was Delivered

1. **Solution & Data Persistence (`Phase 1`):**
   - Solution `RecipeCard.slnx` with web project `RecipeCard.Web` and test project `RecipeCard.Web.Tests`.
   - Core relational models: `Ingredient`, `Recipe`, `RecipeIngredient`, `RecipeStep`.
   - `RecipeDbContext` targeting local SQLite file (`recipe-card.db`).
   - SQLite check constraint `CAST(Quantity AS REAL) > 0` and unique normalized name index for Vietnamese Unicode casing.
   - Initial migration generated and auto-applied on startup.

2. **Ingredient Catalog (`Phase 2`):**
   - Razor Pages for Index, Create, and Edit under `/Ingredients`.
   - Duplicate prevention with normalized key matching.
   - Delete protection blocking removal of ingredients referenced in any recipe with friendly UI alert.

3. **Recipe Composer (`Phase 3`):**
   - Razor Pages for Index, Create, and Edit under `/Recipes`.
   - Dynamic ingredient addition with quantity validation and duplicate ingredient prevention.
   - Step management with photo upload (`.jpg`, `.jpeg`, `.png`, `.webp`, header magic byte verification).
   - Safe 2-pass step reordering inside a transaction avoiding SQLite unique index collisions.
   - Orphan image cleanup on database error.

4. **Preview & PDF Generation (`Phase 4`):**
   - Razor Page preview at `/Recipes/Preview/{id}` enforcing the readiness rule (>= 1 ingredient, >= 1 step).
   - Fixed Phê La-inspired card layout using QuestPDF.
   - Clean downloadable PDF at `/Recipes/Preview/{id}?handler=Pdf`.

5. **Verification & Quality:**
   - 23 unit and integration tests passing (`dotnet test RecipeCard.slnx`).
   - Real PDF parsing assertions with `UglyToad.PdfPig` checking page structure and extracted Vietnamese text.
   - Live smoke test verified over HTTP for page rendering and PDF byte streaming.
   - Seed data included for "Ô Long Sữa Phê La Lạnh".

## Commands to Run

```bash
# Run tests
dotnet test RecipeCard.slnx

# Run web app
dotnet run --project src/RecipeCard.Web
# Open browser at http://localhost:5065
```
