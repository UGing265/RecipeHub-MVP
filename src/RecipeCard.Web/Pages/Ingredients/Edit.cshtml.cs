using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Pages.Ingredients;

public class EditModel(RecipeDbContext db) : PageModel
{
    private readonly RecipeDbContext _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên nguyên liệu không được để trống")]
        [StringLength(100, ErrorMessage = "Tên nguyên liệu không được vượt quá 100 ký tự")]
        [Display(Name = "Tên nguyên liệu")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Đơn vị tính không được để trống")]
        [StringLength(20, ErrorMessage = "Đơn vị tính không được vượt quá 20 ký tự")]
        [Display(Name = "Đơn vị tính mặc định")]
        public string DefaultUnit { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var ingredient = await _db.Ingredients.FindAsync(id);
        if (ingredient == null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Id = ingredient.Id,
            Name = ingredient.Name,
            DefaultUnit = ingredient.DefaultUnit
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var trimmedName = Input.Name?.Trim() ?? string.Empty;
        var trimmedUnit = Input.DefaultUnit?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ModelState.AddModelError("Input.Name", "Tên nguyên liệu không được để trống");
        }

        if (string.IsNullOrWhiteSpace(trimmedUnit))
        {
            ModelState.AddModelError("Input.DefaultUnit", "Đơn vị tính không được để trống");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ingredient = await _db.Ingredients.FindAsync(Input.Id);
        if (ingredient == null)
        {
            return NotFound();
        }

        var normalized = Ingredient.Normalize(trimmedName);

        var exists = await _db.Ingredients
            .AnyAsync(i => i.Id != Input.Id && i.NormalizedName == normalized);
        if (exists)
        {
            ModelState.AddModelError("Input.Name", "Tên nguyên liệu này đã bị trùng với một nguyên liệu khác.");
            return Page();
        }

        ingredient.Name = trimmedName;
        ingredient.NormalizedName = normalized;
        ingredient.DefaultUnit = trimmedUnit;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("Input.Name", "Tên nguyên liệu này đã bị trùng với một nguyên liệu khác.");
            return Page();
        }

        return RedirectToPage("./Index");
    }
}
