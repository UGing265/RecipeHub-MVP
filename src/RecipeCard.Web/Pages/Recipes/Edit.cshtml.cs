using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Pages.Recipes;

public class EditModel : PageModel
{
    private readonly RecipeDbContext _db;
    private readonly IImageStorageService _imageStorage;
    private readonly IImageValidator _validator;
    private readonly IRecipeImageService _recipeImageService;

    public EditModel(
        RecipeDbContext db,
        IImageStorageService imageStorage,
        IImageValidator? validator = null,
        IAiImageGenerator? aiGenerator = null,
        IAiDraftFileStore? draftStore = null,
        IAiImagePromptBuilder? promptBuilder = null,
        IAiPromptTranslator? promptTranslator = null,
        IRecipeImageService? recipeImageService = null,
        Microsoft.Extensions.Options.IOptions<AiImageOptions>? aiOptions = null,
        IAiImageGeneratorRouter? aiRouter = null)
    {
        _db = db;
        _imageStorage = imageStorage;
        _validator = validator ?? new ImageValidator();
        AiOptions = aiOptions?.Value ?? new AiImageOptions();
        RunPodAvailable = AiOptions.RunPodEnabled
            && aiRouter?.IsProviderAvailable(AiImageProvider.RunPodFluxDev) == true;
        EffectiveDefaultProvider = AiOptions.DefaultProvider == AiImageProvider.RunPodFluxDev && !RunPodAvailable
            ? AiImageProvider.CloudflareSchnell
            : AiOptions.DefaultProvider;
        _recipeImageService = recipeImageService ?? new RecipeImageService(
            db,
            promptBuilder ?? new AiImagePromptBuilder(),
            imageStorage,
            _validator,
            aiRouter: aiRouter,
            promptTranslator: promptTranslator,
            draftStore: draftStore,
            legacyAiGenerator: aiGenerator);
    }
    public Recipe Recipe { get; set; } = null!;
    public AiImageOptions AiOptions { get; set; } = new();
    public bool RunPodAvailable { get; }
    public AiImageProvider EffectiveDefaultProvider { get; }
    public List<SelectListItem> AvailableIngredients { get; set; } = [];
    public List<SelectListItem> AvailableMediaAssets { get; set; } = [];
    public AiImageDraft? ActiveFinalDraft { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var recipe = await LoadRecipeGraphAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        Recipe = recipe;
        ActiveFinalDraft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.RecipeId == id && d.TargetKind == AiDraftTargetKind.FinalProduct && d.State == AiDraftState.Generated && d.ExpiresUtc > DateTime.UtcNow);

        await LoadAvailableIngredientsAsync(id);
        await LoadAvailableMediaAssetsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateDetailsAsync(int id, string name, string? generalNote)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            ErrorMessage = "Tên công thức không được để trống.";
            return RedirectToPage(new { id });
        }

        recipe.Name = trimmedName;
        recipe.GeneralNote = string.IsNullOrWhiteSpace(generalNote) ? null : generalNote.Trim();

        await _db.SaveChangesAsync();
        SuccessMessage = "Đã cập nhật thông tin công thức.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddIngredientAsync(int id, int ingredientId, decimal quantity)
    {
        if (quantity <= 0)
        {
            ErrorMessage = "Định lượng nguyên liệu phải lớn hơn 0.";
            return RedirectToPage(new { id });
        }

        var exists = await _db.RecipeIngredients
            .AnyAsync(ri => ri.RecipeId == id && ri.IngredientId == ingredientId);

        if (exists)
        {
            ErrorMessage = "Nguyên liệu này đã có trong công thức. Vui lòng chọn nguyên liệu khác.";
            return RedirectToPage(new { id });
        }

        var line = new RecipeIngredient
        {
            RecipeId = id,
            IngredientId = ingredientId,
            Quantity = quantity
        };

        _db.RecipeIngredients.Add(line);
        await _db.SaveChangesAsync();

        SuccessMessage = "Đã thêm nguyên liệu vào công thức.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveIngredientAsync(int id, int recipeIngredientId)
    {
        var line = await _db.RecipeIngredients
            .FirstOrDefaultAsync(ri => ri.Id == recipeIngredientId && ri.RecipeId == id);

        if (line != null)
        {
            _db.RecipeIngredients.Remove(line);
            await _db.SaveChangesAsync();
            SuccessMessage = "Đã xóa nguyên liệu khỏi công thức.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddStepAsync(int id, string instruction, IFormFile? image, int? mediaAssetId = null)
    {
        var trimmedInstruction = instruction?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedInstruction))
        {
            ErrorMessage = "Nội dung bước thực hiện không được để trống.";
            return RedirectToPage(new { id });
        }

        if (image != null && image.Length > 0 && mediaAssetId.HasValue && mediaAssetId.Value > 0)
        {
            ErrorMessage = "Chỉ được chọn 1 nguồn ảnh (tải ảnh mới HOẶC chọn từ thư viện).";
            return RedirectToPage(new { id });
        }

        var recipe = await _db.Recipes
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        var nextSortOrder = recipe.Steps.Count > 0 ? recipe.Steps.Max(s => s.SortOrder) + 1 : 1;

        var step = new RecipeStep
        {
            RecipeId = id,
            SortOrder = nextSortOrder,
            Instruction = trimmedInstruction
        };

        StoredImage? stored = null;

        if (mediaAssetId.HasValue && mediaAssetId.Value > 0)
        {
            var existingAsset = await _db.MediaAssets.FindAsync(mediaAssetId.Value);
            if (existingAsset != null && existingAsset.State == MediaAssetState.Active)
            {
                step.MediaAssetId = existingAsset.Id;
            }
        }
        else if (image != null && image.Length > 0)
        {
            ValidatedImageInfo info;
            try
            {
                info = _validator.Validate(image);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Ảnh không hợp lệ: {ex.Message}";
                return RedirectToPage(new { id });
            }
            try
            {
                using var stream = image.OpenReadStream();
                stored = await _imageStorage.UploadAsync(stream, info);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Lỗi tải ảnh lên: {ex.Message}";
                return RedirectToPage(new { id });
            }

            var mediaAsset = new MediaAsset
            {
                StorageProvider = "Cloudinary",
                ProviderPublicId = stored.ProviderPublicId,
                DeliveryUrl = stored.DeliveryUrl,
                OriginalFileName = stored.OriginalFileName,
                MimeType = stored.MimeType,
                ByteSize = stored.ByteSize,
                SourceType = MediaSourceType.Real,
                State = MediaAssetState.Active,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.MediaAssets.Add(mediaAsset);
            step.MediaAsset = mediaAsset;
        }

        _db.RecipeSteps.Add(step);

        try
        {
            await _db.SaveChangesAsync();
            SuccessMessage = "Đã thêm bước thực hiện mới.";
        }
        catch
        {
            if (stored != null)
            {
                try
                {
                    await _imageStorage.DeleteAsync(stored.ProviderPublicId);
                }
                catch
                {
                }
            }
            throw;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveStepAsync(int id, int stepId, string direction)
    {
        var steps = await _db.RecipeSteps
            .Where(s => s.RecipeId == id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        var index = steps.FindIndex(s => s.Id == stepId);
        if (index < 0)
        {
            return RedirectToPage(new { id });
        }

        var targetIndex = direction == "up" ? index - 1 : index + 1;
        if (targetIndex < 0 || targetIndex >= steps.Count)
        {
            return RedirectToPage(new { id });
        }

        (steps[index], steps[targetIndex]) = (steps[targetIndex], steps[index]);

        await using var tx = await _db.Database.BeginTransactionAsync();

        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].SortOrder = -(i + 1);
        }
        await _db.SaveChangesAsync();

        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].SortOrder = i + 1;
        }
        await _db.SaveChangesAsync();

        await tx.CommitAsync();

        SuccessMessage = "Đã cập nhật thứ tự các bước.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteStepAsync(int id, int stepId)
    {
        var step = await _db.RecipeSteps
            .FirstOrDefaultAsync(s => s.Id == stepId && s.RecipeId == id);

        if (step != null)
        {
            if (!string.IsNullOrEmpty(step.ImageFileName))
            {
                _imageStorage.DeleteFile(step.ImageFileName);
            }

            _db.RecipeSteps.Remove(step);
            await _db.SaveChangesAsync();

            var remaining = await _db.RecipeSteps
                .Where(s => s.RecipeId == id)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            if (remaining.Count > 0)
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                for (int i = 0; i < remaining.Count; i++)
                {
                    remaining[i].SortOrder = -(i + 1);
                }
                await _db.SaveChangesAsync();

                for (int i = 0; i < remaining.Count; i++)
                {
                    remaining[i].SortOrder = i + 1;
                }
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            SuccessMessage = "Đã xóa bước thực hiện.";
        }

        return RedirectToPage(new { id });
    }

    #region Final Product Hero Image Handlers

    public async Task<IActionResult> OnPostUploadFinalImageAsync(int id, IFormFile? image)
    {
        if (image == null || image.Length == 0)
        {
            ErrorMessage = "Vui lòng chọn một file hình ảnh.";
            return RedirectToPage(new { id });
        }

        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        ValidatedImageInfo info;
        try
        {
            info = _validator.Validate(image);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ảnh không hợp lệ: {ex.Message}";
            return RedirectToPage(new { id });
        }
        StoredImage stored;
        try
        {
            using var stream = image.OpenReadStream();
            stored = await _imageStorage.UploadAsync(stream, info);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Lỗi tải ảnh lên: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var mediaAsset = new MediaAsset
        {
            StorageProvider = "Cloudinary",
            ProviderPublicId = stored.ProviderPublicId,
            DeliveryUrl = stored.DeliveryUrl,
            OriginalFileName = stored.OriginalFileName,
            MimeType = stored.MimeType,
            ByteSize = stored.ByteSize,
            SourceType = MediaSourceType.Real,
            State = MediaAssetState.Active,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.MediaAssets.Add(mediaAsset);
        recipe.FinalMediaAsset = mediaAsset;

        await _db.SaveChangesAsync();
        SuccessMessage = "Đã cập nhật ảnh thành phẩm đại diện cho công thức.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSelectFinalImageFromLibraryAsync(int id, int mediaAssetId)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        var asset = await _db.MediaAssets.FindAsync(mediaAssetId);
        if (asset == null || asset.State != MediaAssetState.Active)
        {
            ErrorMessage = "Ảnh trong thư viện không tồn tại hoặc đã bị ẩn.";
            return RedirectToPage(new { id });
        }

        recipe.FinalMediaAssetId = asset.Id;
        await _db.SaveChangesAsync();

        SuccessMessage = "Đã gắn ảnh từ thư viện làm ảnh thành phẩm đại diện.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveFinalImageAsync(int id)
    {
        var recipe = await _db.Recipes.FindAsync(id);
        if (recipe == null)
        {
            return NotFound();
        }

        recipe.FinalMediaAssetId = null;
        await _db.SaveChangesAsync();

        SuccessMessage = "Đã gỡ ảnh thành phẩm đại diện của công thức.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostGenerateFinalAiImageAsync(int id, AspectRatioPreset? preset, string? userBrief, AiImageProvider? provider = null)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        var result = await _recipeImageService.GenerateFinalProductDraftAsync(id, preset, userBrief, provider, ct);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
        }
        else if (result.SuccessMessage != null)
        {
            SuccessMessage = result.SuccessMessage;
        }

        return RedirectToPage(new { id });
    }

    #endregion

    #region Step AI Image Handlers
    public async Task<IActionResult> OnPostGenerateAiImageAsync(int id, int stepId, string? userBrief, AspectRatioPreset? preset = null, AiImageProvider? provider = null)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        var result = await _recipeImageService.GenerateStepDraftAsync(id, stepId, preset, userBrief, provider, ct);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
        }
        else if (result.SuccessMessage != null)
        {
            SuccessMessage = result.SuccessMessage;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetDraftImageAsync(int id, Guid draftId)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        var result = await _recipeImageService.GetDraftImageAsync(id, draftId, ct);

        if (result.NotFound || !result.Success || result.Bytes == null || result.MimeType == null)
        {
            return NotFound();
        }

        return File(result.Bytes, result.MimeType);
    }
    public async Task<IActionResult> OnPostRegenerateAiImageAsync(int id, Guid draftId, string? userBrief, AspectRatioPreset? preset = null, AiImageProvider? provider = null)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        var result = await _recipeImageService.RegenerateDraftAsync(id, draftId, preset, userBrief, provider, ct);

        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
        }
        else if (result.SuccessMessage != null)
        {
            SuccessMessage = result.SuccessMessage;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDiscardAiImageAsync(int id, Guid draftId)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        var result = await _recipeImageService.DiscardDraftAsync(id, draftId, ct);

        if (result.SuccessMessage != null)
        {
            SuccessMessage = result.SuccessMessage;
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAcceptAiImageAsync(int id, Guid draftId)
    {
        var ct = HttpContext?.RequestAborted ?? default;
        var result = await _recipeImageService.AcceptDraftAsync(id, draftId, ct);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
        }
        else if (result.SuccessMessage != null)
        {
            SuccessMessage = result.SuccessMessage;
        }

        return RedirectToPage(new { id });
    }

    #endregion

    private async Task<Recipe?> LoadRecipeGraphAsync(int id)
    {
        return await _db.Recipes
            .Include(r => r.FinalMediaAsset)
            .Include(r => r.Ingredients)
                .ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps)
                .ThenInclude(s => s.MediaAsset)
            .Include(r => r.Steps)
                .ThenInclude(s => s.AiImageDrafts)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    private async Task LoadAvailableIngredientsAsync(int recipeId)
    {
        var usedIngredientIds = await _db.RecipeIngredients
            .Where(ri => ri.RecipeId == recipeId)
            .Select(ri => ri.IngredientId)
            .ToListAsync();

        AvailableIngredients = await _db.Ingredients
            .Where(i => !usedIngredientIds.Contains(i.Id))
            .OrderBy(i => i.Name)
            .Select(i => new SelectListItem
            {
                Value = i.Id.ToString(),
                Text = $"{i.Name} ({i.DefaultUnit})"
            })
            .ToListAsync();
    }

    private async Task LoadAvailableMediaAssetsAsync()
    {
        AvailableMediaAssets = await _db.MediaAssets
            .Where(m => m.State == MediaAssetState.Active)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new SelectListItem
            {
                Value = m.Id.ToString(),
                Text = $"{(m.SourceType == MediaSourceType.AiIllustration ? "[AI] " : "")}{(string.IsNullOrWhiteSpace(m.OriginalFileName) ? $"Asset #{m.Id}" : m.OriginalFileName)} ({m.CreatedAtUtc:dd/MM})"
            })
            .ToListAsync();
    }
}
