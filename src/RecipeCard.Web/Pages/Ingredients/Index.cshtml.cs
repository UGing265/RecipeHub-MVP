using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Pages.Ingredients;

public class IndexModel(RecipeDbContext db) : PageModel
{
    private readonly RecipeDbContext _db = db;

    public List<Ingredient> Ingredients { get; set; } = [];

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync()
    {
        Ingredients = await _db.Ingredients
            .OrderBy(i => i.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var isUsed = await _db.RecipeIngredients.AnyAsync(ri => ri.IngredientId == id);
        if (isUsed)
        {
            ErrorMessage = "Không thể xóa nguyên liệu đang có trong công thức.";
            return RedirectToPage();
        }

        var ingredient = await _db.Ingredients.FindAsync(id);
        if (ingredient != null)
        {
            try
            {
                _db.Ingredients.Remove(ingredient);
                await _db.SaveChangesAsync();
                SuccessMessage = "Đã xóa nguyên liệu thành công.";
            }
            catch (DbUpdateException)
            {
                ErrorMessage = "Không thể xóa nguyên liệu đang có trong công thức.";
            }
        }

        return RedirectToPage();
    }
}
