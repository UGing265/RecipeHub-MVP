using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Pages.Recipes;

public class EditModel(
    RecipeDbContext db,
    IImageStorageService imageStorage,
    IImageValidator? validator = null,
    IAiImageGenerator? aiGenerator = null,
    IAiDraftFileStore? draftStore = null,
    IAiImagePromptBuilder? promptBuilder = null,
    IAiPromptTranslator? promptTranslator = null) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IImageStorageService _imageStorage = imageStorage;
    private readonly IImageValidator _validator = validator ?? new ImageValidator();
    private readonly IAiImageGenerator? _aiGenerator = aiGenerator;
    private readonly IAiDraftFileStore? _draftStore = draftStore;
    private readonly IAiImagePromptBuilder _promptBuilder = promptBuilder ?? new AiImagePromptBuilder();
    private readonly IAiPromptTranslator? _promptTranslator = promptTranslator;

    public Recipe Recipe { get; set; } = null!;
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

    public async Task<IActionResult> OnPostGenerateFinalAiImageAsync(int id, AspectRatioPreset? preset, string? userBrief)
    {
        if (_aiGenerator == null || _draftStore == null)
        {
            ErrorMessage = "Dịch vụ AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        if (_promptTranslator == null)
        {
            ErrorMessage = "Dịch vụ dịch prompt AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        var recipe = await _db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        var activeDraft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.RecipeId == id && d.TargetKind == AiDraftTargetKind.FinalProduct && d.State == AiDraftState.Generated && d.ExpiresUtc > DateTime.UtcNow);

        if (activeDraft != null)
        {
            ErrorMessage = "Công thức đã có một ảnh thành phẩm nháp AI đang chờ duyệt. Vui lòng chấp nhận hoặc bỏ ảnh trước khi tạo mới.";
            return RedirectToPage(new { id });
        }

        var targetPreset = preset ?? recipe.FinalImageAspectRatioPreset;
        recipe.FinalImageAspectRatioPreset = targetPreset;

        var ct = HttpContext?.RequestAborted ?? default;

        string sourcePrompt;
        try
        {
            var ings = recipe.Ingredients.Select(ri => (ri.Ingredient.Name, ri.Quantity, ri.Ingredient.DefaultUnit));
            var steps = recipe.Steps.OrderBy(s => s.SortOrder).Select(s => (s.SortOrder, s.Instruction));
            sourcePrompt = _promptBuilder.BuildFinalProductSourcePrompt(recipe.Name, ings, steps, recipe.GeneralNote, userBrief);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dựng prompt thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        string translated;
        try
        {
            translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dịch mô tả cho AI thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        string fullPrompt;
        try
        {
            fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.FinalProduct, translated);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToPage(new { id });
        }

        var (w, h) = targetPreset.ToDimensions();
        var request = new AiImageGenerationRequest(fullPrompt, targetPreset, w, h);

        GeneratedImage generated;
        try
        {
            generated = await _aiGenerator.GenerateAsync(request, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tạo ảnh AI thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var draftId = Guid.NewGuid();
        string tempFileName;
        try
        {
            tempFileName = await _draftStore.SaveDraftAsync(draftId, generated.ImageBytes, ".jpg");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeId = id,
            RecipeStepId = null,
            TargetKind = AiDraftTargetKind.FinalProduct,
            AspectRatioPreset = targetPreset,
            PromptSnapshot = fullPrompt,
            UserBrief = string.IsNullOrWhiteSpace(userBrief) ? null : userBrief.Trim(),
            Model = generated.Model,
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };

        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        SuccessMessage = "Đã tạo ảnh nháp AI thành phẩm thành công. Hãy xem lại và chọn Chấp nhận hoặc Bỏ ảnh.";
        return RedirectToPage(new { id });
    }

    #endregion

    #region Step AI Image Handlers

    public async Task<IActionResult> OnPostGenerateAiImageAsync(int id, int stepId, string? userBrief, AspectRatioPreset? preset = null)
    {
        if (_aiGenerator == null || _draftStore == null)
        {
            ErrorMessage = "Dịch vụ AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        if (_promptTranslator == null)
        {
            ErrorMessage = "Dịch vụ dịch prompt AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        var recipe = await _db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps).ThenInclude(s => s.AiImageDrafts)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
        {
            return NotFound();
        }

        var step = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
        if (step == null)
        {
            return NotFound();
        }

        var activeDraft = step.AiImageDrafts
            .FirstOrDefault(d => d.State == AiDraftState.Generated && d.ExpiresUtc > DateTime.UtcNow);
        if (activeDraft != null)
        {
            ErrorMessage = "Bước này đã có một ảnh nháp AI đang chờ duyệt. Vui lòng chấp nhận hoặc bỏ ảnh hiện tại trước khi tạo mới.";
            return RedirectToPage(new { id });
        }

        var targetPreset = preset ?? step.ImageAspectRatioPreset;
        step.ImageAspectRatioPreset = targetPreset;

        var ct = HttpContext?.RequestAborted ?? default;

        string sourcePrompt;
        try
        {
            var ings = recipe.Ingredients.Select(ri => (ri.Ingredient.Name, ri.Quantity, ri.Ingredient.DefaultUnit));
            var priorSteps = recipe.Steps
                .Where(s => s.SortOrder < step.SortOrder)
                .OrderBy(s => s.SortOrder)
                .Select(s => (s.SortOrder, s.Instruction));

            sourcePrompt = _promptBuilder.BuildStepSourcePrompt(
                recipe.Name,
                ings,
                priorSteps,
                step.SortOrder,
                step.Instruction,
                userBrief);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dựng prompt thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        string translated;
        try
        {
            translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Dịch mô tả cho AI thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        string fullPrompt;
        try
        {
            fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.StepInstruction, translated);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return RedirectToPage(new { id });
        }

        var (w, h) = targetPreset.ToDimensions();
        var request = new AiImageGenerationRequest(fullPrompt, targetPreset, w, h);

        GeneratedImage generated;
        try
        {
            generated = await _aiGenerator.GenerateAsync(request, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tạo ảnh AI thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var draftId = Guid.NewGuid();
        string tempFileName;
        try
        {
            tempFileName = await _draftStore.SaveDraftAsync(draftId, generated.ImageBytes, ".jpg");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeId = id,
            RecipeStepId = stepId,
            TargetKind = AiDraftTargetKind.StepInstruction,
            AspectRatioPreset = targetPreset,
            PromptSnapshot = fullPrompt,
            UserBrief = string.IsNullOrWhiteSpace(userBrief) ? null : userBrief.Trim(),
            Model = generated.Model,
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        SuccessMessage = "Đã tạo ảnh nháp AI thành công. Hãy xem lại và chọn Chấp nhận hoặc Bỏ ảnh.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetDraftImageAsync(int id, Guid draftId)
    {
        if (_draftStore == null)
        {
            return NotFound();
        }

        var draft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == id);

        if (draft == null || draft.State != AiDraftState.Generated || draft.ExpiresUtc <= DateTime.UtcNow)
        {
            return NotFound();
        }

        try
        {
            var bytes = await _draftStore.ReadDraftAsync(draft.TemporaryFileName);
            var info = _validator.ValidateBytes(bytes, draft.TemporaryFileName);
            return File(bytes, info.MimeType);
        }
        catch
        {
            return NotFound();
        }
    }

    public async Task<IActionResult> OnPostRegenerateAiImageAsync(int id, Guid draftId, string? userBrief, AspectRatioPreset? preset = null)
    {
        if (_aiGenerator == null || _draftStore == null)
        {
            ErrorMessage = "Dịch vụ AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        if (_promptTranslator == null)
        {
            ErrorMessage = "Dịch vụ dịch prompt AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        var currentDraft = await _db.AiImageDrafts
            .Include(d => d.RecipeStep)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == id);

        if (currentDraft == null || currentDraft.State != AiDraftState.Generated)
        {
            ErrorMessage = "Bản nháp AI không tồn tại hoặc đã được xử lý trước đó.";
            return RedirectToPage(new { id });
        }

        if (currentDraft.ExpiresUtc <= DateTime.UtcNow)
        {
            currentDraft.State = AiDraftState.Expired;
            _draftStore.DeleteDraft(currentDraft.TemporaryFileName);
            await _db.SaveChangesAsync();
            ErrorMessage = "Ảnh nháp AI đã hết hạn. Vui lòng tạo lại ảnh mới.";
            return RedirectToPage(new { id });
        }

        var targetPreset = preset ?? currentDraft.AspectRatioPreset;

        var ct = HttpContext?.RequestAborted ?? default;
        string fullPrompt;

        if (currentDraft.TargetKind == AiDraftTargetKind.FinalProduct)
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
            {
                return NotFound();
            }

            var ings = recipe.Ingredients.Select(ri => (ri.Ingredient.Name, ri.Quantity, ri.Ingredient.DefaultUnit));
            var steps = recipe.Steps.OrderBy(s => s.SortOrder).Select(s => (s.SortOrder, s.Instruction));
            var sourcePrompt = _promptBuilder.BuildFinalProductSourcePrompt(recipe.Name, ings, steps, recipe.GeneralNote, userBrief);
            string translated;
            try
            {
                translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, ct);
                fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.FinalProduct, translated);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Dịch mô tả cho AI thất bại: {ex.Message}";
                return RedirectToPage(new { id });
            }
            recipe.FinalImageAspectRatioPreset = targetPreset;
        }
        else
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recipe == null)
            {
                return NotFound();
            }

            var step = recipe.Steps.FirstOrDefault(s => s.Id == currentDraft.RecipeStepId);
            if (step == null)
            {
                return NotFound();
            }

            var ings = recipe.Ingredients.Select(ri => (ri.Ingredient.Name, ri.Quantity, ri.Ingredient.DefaultUnit));
            var priorSteps = recipe.Steps
                .Where(s => s.SortOrder < step.SortOrder)
                .OrderBy(s => s.SortOrder)
                .Select(s => (s.SortOrder, s.Instruction));

            var sourcePrompt = _promptBuilder.BuildStepSourcePrompt(recipe.Name, ings, priorSteps, step.SortOrder, step.Instruction, userBrief);
            string translated;
            try
            {
                translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, ct);
                fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.StepInstruction, translated);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Dịch mô tả cho AI thất bại: {ex.Message}";
                return RedirectToPage(new { id });
            }
            step.ImageAspectRatioPreset = targetPreset;
        }

        var (w, h) = targetPreset.ToDimensions();
        var request = new AiImageGenerationRequest(fullPrompt, targetPreset, w, h);

        GeneratedImage generated;
        try
        {
            generated = await _aiGenerator.GenerateAsync(request, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tạo ảnh AI thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var newDraftId = Guid.NewGuid();
        string tempFileName;
        try
        {
            tempFileName = await _draftStore.SaveDraftAsync(newDraftId, generated.ImageBytes, ".jpg");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}";
            return RedirectToPage(new { id });
        }

        _draftStore.DeleteDraft(currentDraft.TemporaryFileName);
        currentDraft.State = AiDraftState.Discarded;

        var newDraft = new AiImageDraft
        {
            Id = newDraftId,
            RecipeId = id,
            RecipeStepId = currentDraft.RecipeStepId,
            TargetKind = currentDraft.TargetKind,
            AspectRatioPreset = targetPreset,
            PromptSnapshot = fullPrompt,
            UserBrief = string.IsNullOrWhiteSpace(userBrief) ? null : userBrief.Trim(),
            Model = generated.Model,
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };

        _db.AiImageDrafts.Add(newDraft);
        await _db.SaveChangesAsync();

        SuccessMessage = "Đã tạo lại ảnh nháp AI mới.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDiscardAiImageAsync(int id, Guid draftId)
    {
        var draft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == id);

        if (draft != null)
        {
            if (_draftStore != null)
            {
                _draftStore.DeleteDraft(draft.TemporaryFileName);
            }
            draft.State = AiDraftState.Discarded;
            await _db.SaveChangesAsync();
            SuccessMessage = "Đã bỏ ảnh nháp AI.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAcceptAiImageAsync(int id, Guid draftId)
    {
        if (_draftStore == null)
        {
            ErrorMessage = "Dịch vụ lưu trữ nháp chưa sẵn sàng.";
            return RedirectToPage(new { id });
        }

        var draft = await _db.AiImageDrafts
            .Include(d => d.RecipeStep)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == id);

        if (draft == null || draft.State != AiDraftState.Generated)
        {
            ErrorMessage = "Bản nháp AI không tồn tại hoặc đã được xử lý trước đó.";
            return RedirectToPage(new { id });
        }

        if (draft.ExpiresUtc <= DateTime.UtcNow)
        {
            draft.State = AiDraftState.Expired;
            _draftStore.DeleteDraft(draft.TemporaryFileName);
            await _db.SaveChangesAsync();
            ErrorMessage = "Ảnh nháp AI đã hết hạn. Vui lòng tạo lại ảnh mới.";
            return RedirectToPage(new { id });
        }

        byte[] draftBytes;
        try
        {
            draftBytes = await _draftStore.ReadDraftAsync(draft.TemporaryFileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không thể đọc file ảnh nháp: {ex.Message}";
            return RedirectToPage(new { id });
        }

        ValidatedImageInfo info;
        try
        {
            info = _validator.ValidateBytes(draftBytes, draft.TemporaryFileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ảnh nháp không đạt chuẩn hợp lệ: {ex.Message}";
            return RedirectToPage(new { id });
        }

        StoredImage stored;
        try
        {
            using var stream = new MemoryStream(draftBytes);
            stored = await _imageStorage.UploadAsync(stream, info);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tải ảnh lên máy chủ lưu trữ thất bại: {ex.Message}";
            return RedirectToPage(new { id });
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var mediaAsset = new MediaAsset
            {
                StorageProvider = "Cloudinary",
                ProviderPublicId = stored.ProviderPublicId,
                DeliveryUrl = stored.DeliveryUrl,
                OriginalFileName = $"ai-{draft.Id:N}.jpg",
                MimeType = stored.MimeType,
                ByteSize = stored.ByteSize,
                SourceType = MediaSourceType.AiIllustration,
                State = MediaAssetState.Active,
                CreatedAtUtc = DateTime.UtcNow,
                AiImageDraftId = draft.Id
            };

            _db.MediaAssets.Add(mediaAsset);

            if (draft.TargetKind == AiDraftTargetKind.FinalProduct)
            {
                var recipe = await _db.Recipes.FindAsync(id);
                if (recipe != null)
                {
                    recipe.FinalMediaAsset = mediaAsset;
                    recipe.FinalImageAspectRatioPreset = draft.AspectRatioPreset;
                }
            }
            else if (draft.RecipeStep != null)
            {
                var step = draft.RecipeStep;
                step.MediaAsset = mediaAsset;
                step.ImageAspectRatioPreset = draft.AspectRatioPreset;
            }

            draft.State = AiDraftState.Accepted;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _draftStore.DeleteDraft(draft.TemporaryFileName);
            SuccessMessage = draft.TargetKind == AiDraftTargetKind.FinalProduct
                ? "Đã chấp nhận và gắn ảnh AI làm ảnh thành phẩm đại diện cho công thức."
                : "Đã chấp nhận và gắn ảnh minh họa AI vào bước công thức thành công.";
        }
        catch
        {
            await tx.RollbackAsync();
            try
            {
                await _imageStorage.DeleteAsync(stored.ProviderPublicId);
            }
            catch
            {
            }
            throw;
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
