using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pdf;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Pages.Recipes;
public partial class PreviewModel(
    RecipeDbContext db,
    IRecipePdfModelFactory pdfModelFactory) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IRecipePdfModelFactory _pdfModelFactory = pdfModelFactory;
    public Recipe Recipe { get; set; } = null!;
    public bool IsReady => Recipe != null && Recipe.Ingredients.Count > 0 && Recipe.Steps.Count > 0;

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var recipe = await LoadRecipeAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        Recipe = recipe;
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        var recipe = await LoadRecipeAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        Recipe = recipe;

        if (!IsReady)
        {
            ErrorMessage = "Công thức chưa đủ điều kiện xuất PDF. Cần có ít nhất 1 nguyên liệu và 1 bước thực hiện.";
            return RedirectToPage(new { id });
        }

        await using var batch = await _pdfModelFactory.CreateAsync([recipe], HttpContext?.RequestAborted ?? CancellationToken.None);
        var doc = new RecipePdfDocument(batch.Models.Single());
        var pdfBytes = doc.GeneratePdf();

        var slug = SlugRegex().Replace(recipe.Name, "-").Trim('-').ToLowerInvariant();
        if (string.IsNullOrEmpty(slug))
        {
            slug = "cong-thuc";
        }

        return File(pdfBytes, "application/pdf", $"{slug}.pdf");
    }

    private async Task<Recipe?> LoadRecipeAsync(int id)
    {
        return await _db.Recipes
            .Include(r => r.FinalMediaAsset)
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MediaAsset)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_\u00C0-\u024F\u1EA0-\u1EF9]+")]
    private static partial Regex SlugRegex();
}
