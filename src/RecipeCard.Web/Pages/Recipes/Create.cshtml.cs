using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Pages.Recipes;

public class CreateModel(RecipeDbContext db) : PageModel
{
    private readonly RecipeDbContext _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Tên công thức không được để trống")]
        [StringLength(200, ErrorMessage = "Tên công thức không được vượt quá 200 ký tự")]
        [Display(Name = "Tên công thức pha chế")]
        public string Name { get; set; } = string.Empty;

        [StringLength(2000, ErrorMessage = "Ghi chú không được vượt quá 2000 ký tự")]
        [Display(Name = "Ghi chú chung / Hướng dẫn phục vụ")]
        public string? GeneralNote { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var trimmedName = Input.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError("Input.Name", "Tên công thức không được để trống");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var recipe = new Recipe
        {
            Name = trimmedName,
            GeneralNote = string.IsNullOrWhiteSpace(Input.GeneralNote) ? null : Input.GeneralNote.Trim()
        };

        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        return RedirectToPage("./Edit", new { id = recipe.Id });
    }
}
