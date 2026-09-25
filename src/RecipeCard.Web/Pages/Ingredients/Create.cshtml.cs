using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Pages.Ingredients;

public class CreateModel(RecipeDbContext db) : PageModel
{
    private readonly RecipeDbContext _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Tên nguyên liệu không được để trống")]
        [StringLength(100, ErrorMessage = "Tên nguyên liệu không được vượt quá 100 ký tự")]
        [Display(Name = "Tên nguyên liệu")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Đơn vị tính không được để trống")]
        [StringLength(20, ErrorMessage = "Đơn vị tính không được vượt quá 20 ký tự")]
        [Display(Name = "Đơn vị tính mặc định")]
        public string DefaultUnit { get; set; } = string.Empty;
    }

    public void OnGet()
    {
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

        var normalized = Ingredient.Normalize(trimmedName);

        var exists = await _db.Ingredients.AnyAsync(i => i.NormalizedName == normalized);
        if (exists)
        {
            ModelState.AddModelError("Input.Name", "Nguyên liệu này đã tồn tại trong danh mục.");
            return Page();
        }

        var ingredient = new Ingredient
        {
            Name = trimmedName,
            NormalizedName = normalized,
            DefaultUnit = trimmedUnit
        };

        _db.Ingredients.Add(ingredient);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError("Input.Name", "Nguyên liệu này đã tồn tại trong danh mục.");
            return Page();
        }

        return RedirectToPage("./Index");
    }
}
