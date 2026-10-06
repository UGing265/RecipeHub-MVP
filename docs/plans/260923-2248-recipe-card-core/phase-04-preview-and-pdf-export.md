# Phase 4 — Preview and PDF export

## Context

- Parent: [plan.md](./plan.md)
- Depends on: [phase-03-recipe-composer.md](./phase-03-recipe-composer.md)
- Reference: `docs/materials/PRN232.SRS.md:265-274, 408`

## Overview

Build the HTML preview card and the server-side PDF generator for a recipe. The output follows a fixed, readable recipe-card layout modeled after Phê La operational cards.

## Requirements

- A recipe cannot be previewed or exported to PDF unless it has at least one ingredient line and at least one step.
- Incomplete recipes display a clear readiness alert with missing requirements and disable the PDF download action.
- The HTML preview and the exported PDF render the same structured content:
  - Title and optional general note.
  - Table of ingredients with name, quantity, and unit.
  - Sequential steps with step number, instruction text, and step photo if attached.
- PDF generation is performed on the server with QuestPDF, outputting standard PDF bytes.
- The downloaded file is named `{sanitized-recipe-name}.pdf`.
- Missing or deleted image files on disk do not break PDF export; the step renders its text with the image placeholder omitted.

## Files

| Action | Path | Responsibility |
|---|---|---|
| Create | `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml` | Browser view of the recipe card with print styling and Download PDF button. |
| Create | `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs` | Load recipe, enforce completeness check, dispatch PDF generation. |
| Create | `src/RecipeCard.Web/Pdf/RecipePdfDocument.cs` | QuestPDF document template producing the fixed-layout recipe card. |
| Modify | `src/RecipeCard.Web/Program.cs` | Configure QuestPDF community license. |
| Create | `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` | Tests for readiness rule enforcement and non-empty valid PDF byte generation. |

## Implementation Steps

1. Configure QuestPDF:
   - Add pinned NuGet package: `QuestPDF` (latest stable 2024.12.x or 2025.x).
   - Licensing prerequisite:
     - QuestPDF Community License is free for open-source and organizations with annual revenue < $1,000,000 USD.
     - In `Program.cs`, read configuration `QuestPdf:LicenseType` (default `"Community"`):
       ```csharp
       QuestPDF.Settings.License = builder.Configuration["QuestPdf:LicenseType"] == "Commercial"
           ? LicenseType.Enterprise
           : LicenseType.Community;
       ```
2. Implement `RecipePdfDocument`:
   - Accept a validated recipe data model (title, notes, ingredients list, steps list with physical image paths).
   - Configure page: A4 portrait, 20mm margins, clean sans-serif typography.
   - Header: Recipe title in bold, creation date, subtitle.
   - Section 1 — "Thành phần nguyên liệu": table with columns `STT`, `Nguyên liệu`, `Định lượng`, `Đơn vị`.
   - Section 2 — "Quy trình thực hiện": vertical list of steps. Each step renders a rounded number badge, the instruction body, and an inline photo (constrained to max height 180pt) if the image exists on disk.
   - Section 3 — "Lưu ý & Phục vụ": box with `GeneralNote` if present.
   - Footer: page numbers `"Trang X / Y"`.

3. Implement `Preview.cshtml.cs`:
   - `OnGetAsync(int id)`: load recipe, ingredients, and steps.
   - Compute `bool IsReady = Recipe.Ingredients.Count > 0 && Recipe.Steps.Count > 0`.
   - `OnGetPdfAsync(int id)`:
     - Check `IsReady`; if false, redirect to `Preview` with an error message.
     - Generate PDF bytes via `RecipePdfDocument.GeneratePdf()`.
     - Return `File(pdfBytes, "application/pdf", $"{slug}.pdf")`.

4. Build `Preview.cshtml`:
   - When not ready, display alert: `"Công thức cần ít nhất 1 nguyên liệu và 1 bước thực hiện để xuất PDF."`
   - Render preview card styled with clean borders, structured tables, and step photos.
   - Provide a prominent `"Tải PDF"` button linking to handler `?handler=Pdf`.

5. Add export tests in `RecipePdfExportTests.cs`:
   - Attempting to export a recipe with zero ingredients returns validation failure without generating bytes.
   - Attempting to export a recipe with zero steps returns validation failure without generating bytes.
   - Exporting a complete recipe produces a valid PDF:
     - Byte array begins with `%PDF-`.
     - Stream is parseable and contains at least 1 readable page without layout engine exceptions.
   - A step with a non-existent or corrupted image path still generates a valid PDF with instruction text intact (graceful fallback).
## Success Criteria

- User opens preview and sees their recipe formatted like an operational card.
- Incomplete recipes cannot generate a PDF; user is guided back to edit.
- Clicking "Tải PDF" downloads a valid PDF that opens in any standard viewer.
- PDF includes ingredient table, sequential instructions, and step images without clipping or distortion.
- All PDF export tests pass.

## Todo

- [ ] Add QuestPDF dependency and license initialization.
- [ ] Implement `RecipePdfDocument` with fixed Phê La-style card layout.
- [ ] Implement `Preview.cshtml` and `Preview.cshtml.cs` with readiness gate.
- [ ] Add and pass `RecipePdfExportTests`.
