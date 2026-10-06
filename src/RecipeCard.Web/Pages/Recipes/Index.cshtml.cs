using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Pages.Recipes;

public class IndexModel(RecipeDbContext db, IImageStorageService imageStorage) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IImageStorageService _imageStorage = imageStorage;

    public List<RecipeListItem> Recipes { get; set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    public sealed class RecipeListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? GeneralNote { get; set; }
        public int IngredientCount { get; set; }
        public int StepCount { get; set; }
        public string? FinalImageUrl { get; set; }
        public bool IsReady => IngredientCount > 0 && StepCount > 0;
    }

    public async Task OnGetAsync()
    {
        Recipes = await _db.Recipes
            .OrderByDescending(r => r.Id)
            .Select(r => new RecipeListItem
            {
                Id = r.Id,
                Name = r.Name,
                GeneralNote = r.GeneralNote,
                IngredientCount = r.Ingredients.Count,
                StepCount = r.Steps.Count,
                FinalImageUrl = r.FinalMediaAsset != null ? r.FinalMediaAsset.DeliveryUrl : null
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe != null)
        {
            // Collect images to delete
            var imagesToDelete = recipe.Steps
                .Select(s => s.ImageFileName)
                .Where(img => !string.IsNullOrEmpty(img))
                .ToList();

            _db.Recipes.Remove(recipe);
            await _db.SaveChangesAsync();

            // Clean up files from disk
            foreach (var img in imagesToDelete)
            {
                _imageStorage.DeleteFile(img);
            }

            SuccessMessage = $"Đã xóa công thức \"{recipe.Name}\" thành công.";
        }

        return RedirectToPage();
    }
}
