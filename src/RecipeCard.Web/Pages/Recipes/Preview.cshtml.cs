using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pdf;

namespace RecipeCard.Web.Pages.Recipes;

public partial class PreviewModel(
    RecipeDbContext db,
    IWebHostEnvironment environment,
    IHttpClientFactory? httpClientFactory = null) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IWebHostEnvironment _env = environment;
    private readonly IHttpClientFactory? _httpClientFactory = httpClientFactory;
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

        var webRoot = _env.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }

        var pdfModel = new RecipePdfModel
        {
            Title = recipe.Name,
            GeneralNote = recipe.GeneralNote,
            Ingredients = recipe.Ingredients
                .Select((ri, idx) => new RecipePdfIngredientLine
                {
                    Stt = idx + 1,
                    Name = ri.Ingredient.Name,
                    Quantity = ri.Quantity,
                    Unit = ri.Ingredient.DefaultUnit
                })
                .ToList(),
            Steps = []
        };

        foreach (var s in recipe.Steps.OrderBy(s => s.SortOrder))
        {
            var stepLine = new RecipePdfStepLine
            {
                StepNumber = s.SortOrder,
                Instruction = s.Instruction
            };

            if (s.MediaAsset != null && !string.IsNullOrEmpty(s.MediaAsset.DeliveryUrl))
            {
                stepLine.IsAiIllustration = s.MediaAsset.SourceType == MediaSourceType.AiIllustration;
                if (_httpClientFactory != null)
                {
                    try
                    {
                        using var client = _httpClientFactory.CreateClient("MediaDelivery");
                        using var response = await client.GetAsync(s.MediaAsset.DeliveryUrl, HttpCompletionOption.ResponseHeadersRead);
                        if (response.IsSuccessStatusCode)
                        {
                            stepLine.ImageBytes = await response.Content.ReadAsByteArrayAsync();
                        }
                    }
                    catch
                    {
                        // Graceful fallback: PDF does not fail if remote image cannot be fetched
                    }
                }
            }
            else if (!string.IsNullOrEmpty(s.ImageFileName))
            {
                stepLine.ImageFullPath = Path.Combine(webRoot, "uploads", "steps", s.ImageFileName);
            }

            pdfModel.Steps.Add(stepLine);
        }

        var doc = new RecipePdfDocument(pdfModel);
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
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MediaAsset)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_\u00C0-\u024F\u1EA0-\u1EF9]+")]
    private static partial Regex SlugRegex();
}
