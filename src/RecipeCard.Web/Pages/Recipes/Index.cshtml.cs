using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pdf;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Pages.Recipes;

public class IndexModel(
    RecipeDbContext db,
    IImageStorageService imageStorage,
    IRecipePdfModelFactory pdfModelFactory,
    IWebHostEnvironment environment,
    TimeProvider timeProvider) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IImageStorageService _imageStorage = imageStorage;
    private readonly IRecipePdfModelFactory _pdfModelFactory = pdfModelFactory;
    private readonly IWebHostEnvironment _env = environment;
    private readonly TimeProvider _timeProvider = timeProvider;

    private const int MaxSelectedRecipes = 50;

    [BindProperty]
    public List<int> SelectedRecipeIds { get; set; } = [];

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

    public async Task OnGetAsync(CancellationToken cancellationToken = default)
    {
        await LoadRecipesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostExportSelectedAsync(CancellationToken cancellationToken)
    {
        SelectedRecipeIds ??= [];

        // 1. Model binding error or non-positive ID
        if (!ModelState.IsValid || SelectedRecipeIds.Any(id => id <= 0))
        {
            ModelState.Clear();
            return await RejectExportAsync("Dữ liệu lựa chọn công thức không hợp lệ.", cancellationToken);
        }

        // 2. Empty list
        if (SelectedRecipeIds.Count == 0)
        {
            return await RejectExportAsync("Hãy chọn ít nhất 1 công thức để xuất PDF.", cancellationToken);
        }

        // 3. More than 50 items
        if (SelectedRecipeIds.Count > MaxSelectedRecipes)
        {
            return await RejectExportAsync("Chỉ có thể xuất tối đa 50 công thức mỗi lần.", cancellationToken);
        }

        // 4. Duplicate IDs
        if (SelectedRecipeIds.Distinct().Count() != SelectedRecipeIds.Count)
        {
            return await RejectExportAsync("Danh sách công thức có ID trùng lặp. Vui lòng chọn lại.", cancellationToken);
        }

        // 5. Query DB using AsSplitQuery + AsNoTracking
        var foundRecipes = await _db.Recipes
            .AsNoTracking()
            .AsSplitQuery()
            .Where(r => SelectedRecipeIds.Contains(r.Id))
            .Include(r => r.FinalMediaAsset)
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MediaAsset)
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        // Check for missing IDs (precedence 5)
        var foundIds = foundRecipes.Select(r => r.Id).ToHashSet();
        var missingIds = SelectedRecipeIds
            .Where(id => !foundIds.Contains(id))
            .OrderByDescending(id => id)
            .ToList();

        if (missingIds.Count > 0)
        {
            var missingListStr = string.Join(", ", missingIds);
            return await RejectExportAsync($"Không tìm thấy công thức đã chọn: {missingListStr}.", cancellationToken);
        }

        // 6. Check readiness of all recipes (precedence 6)
        var notReadyRecipes = foundRecipes
            .Where(r => r.Ingredients.Count == 0 || r.Steps.Count == 0)
            .OrderByDescending(r => r.Id)
            .ToList();

        if (notReadyRecipes.Count > 0)
        {
            var details = string.Join(", ", notReadyRecipes.Select(r => $"{r.Name} (#{r.Id})"));
            return await RejectExportAsync($"Công thức chưa đủ điều kiện xuất PDF: {details}", cancellationToken);
        }

        // All valid! Execute export with recipes strictly sorted by Id DESC
        var webRoot = _env.WebRootPath;
        if (string.IsNullOrEmpty(webRoot))
        {
            webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }

        var logoPath = Path.Combine(webRoot, "images", "brand", "phe-la-logo.svg");

        await using var batch = await _pdfModelFactory.CreateAsync(foundRecipes, cancellationToken);

        var exportedAt = _timeProvider.GetLocalNow();
        var bookletModel = new RecipeBookletPdfModel(
            Recipes: batch.Models,
            ExportedAt: exportedAt,
            OfficialLogoSvgPath: logoPath);
        cancellationToken.ThrowIfCancellationRequested();
        var doc = new RecipeBookletPdfDocument(bookletModel);
        var pdfBytes = doc.GeneratePdf();
        var fileName = $"recipe-collection-{exportedAt:yyyyMMdd-HHmm}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    private async Task<IActionResult> RejectExportAsync(string message, CancellationToken cancellationToken)
    {
        ModelState.Clear();
        ModelState.AddModelError(string.Empty, message);
        Response.StatusCode = StatusCodes.Status400BadRequest;

        await LoadRecipesAsync(cancellationToken);

        // Retain only IDs that exist in the loaded list and are ready
        var readyIdSet = Recipes.Where(r => r.IsReady).Select(r => r.Id).ToHashSet();
        SelectedRecipeIds = SelectedRecipeIds.Where(readyIdSet.Contains).ToList();

        return Page();
    }

    private async Task LoadRecipesAsync(CancellationToken cancellationToken)
    {
        Recipes = await _db.Recipes
            .AsNoTracking()
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
            .ToListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var recipe = await _db.Recipes
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe != null)
        {
            var imagesToDelete = recipe.Steps
                .Select(s => s.ImageFileName)
                .Where(img => !string.IsNullOrEmpty(img))
                .ToList();

            _db.Recipes.Remove(recipe);
            await _db.SaveChangesAsync();

            foreach (var img in imagesToDelete)
            {
                _imageStorage.DeleteFile(img);
            }

            SuccessMessage = $"Đã xóa công thức \"{recipe.Name}\" thành công.";
        }

        return RedirectToPage();
    }
}
