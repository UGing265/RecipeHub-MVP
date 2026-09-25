using System.ComponentModel.DataAnnotations;
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
    IAiImagePromptBuilder? promptBuilder = null) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IImageStorageService _imageStorage = imageStorage;
    private readonly IImageValidator _validator = validator ?? new ImageValidator();
    private readonly IAiImageGenerator? _aiGenerator = aiGenerator;
    private readonly IAiDraftFileStore? _draftStore = draftStore;
    private readonly IAiImagePromptBuilder _promptBuilder = promptBuilder ?? new AiImagePromptBuilder();
    public Recipe Recipe { get; set; } = null!;
    public List<SelectListItem> AvailableIngredients { get; set; } = [];
    public List<SelectListItem> AvailableMediaAssets { get; set; } = [];
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
            SuccessMessage = "Đã xóa dòng nguyên liệu.";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAddStepAsync(int id, string instruction, IFormFile? image, int? mediaAssetId = null)
    {
        var trimmedInstruction = instruction?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedInstruction))
        {
            ErrorMessage = "Mô tả bước thực hiện không được để trống.";
            return RedirectToPage(new { id });
        }

        if (image != null && image.Length > 0 && mediaAssetId.HasValue && mediaAssetId.Value > 0)
        {
            ErrorMessage = "Chỉ được chọn 1 nguồn ảnh: tải ảnh mới hoặc chọn từ thư viện.";
            return RedirectToPage(new { id });
        }

        var currentMaxOrder = await _db.RecipeSteps
            .Where(s => s.RecipeId == id)
            .MaxAsync(s => (int?)s.SortOrder) ?? 0;

        var step = new RecipeStep
        {
            RecipeId = id,
            Instruction = trimmedInstruction,
            SortOrder = currentMaxOrder + 1
        };

        if (mediaAssetId.HasValue && mediaAssetId.Value > 0)
        {
            var asset = await _db.MediaAssets
                .FirstOrDefaultAsync(a => a.Id == mediaAssetId.Value && a.State == MediaAssetState.Active);
            if (asset == null)
            {
                ErrorMessage = "Tài nguyên ảnh được chọn không tồn tại hoặc đã bị xóa.";
                return RedirectToPage(new { id });
            }
            step.MediaAssetId = asset.Id;
        }

        StoredImage? stored = null;
        if (image != null && image.Length > 0)
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
                    // Best-effort compensation
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
            return RedirectToPage(new { id }); // Already at top or bottom
        }

        // Swap items in memory list
        (steps[index], steps[targetIndex]) = (steps[targetIndex], steps[index]);

        // Two-pass update inside a transaction to prevent SQLite unique index collision on (RecipeId, SortOrder)
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Pass 1: Assign temporary negative orders
        for (int i = 0; i < steps.Count; i++)
        {
            steps[i].SortOrder = -(i + 1);
        }
        await _db.SaveChangesAsync();

        // Pass 2: Assign clean positive sequential orders 1..N
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

            // Renumber remaining steps cleanly
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

    public async Task<IActionResult> OnPostGenerateAiImageAsync(int id, int stepId, string? userBrief)
    {
        if (_aiGenerator == null || _draftStore == null)
        {
            ErrorMessage = "Dịch vụ AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        var step = await _db.RecipeSteps
            .Include(s => s.Recipe)
            .Include(s => s.AiImageDrafts)
            .FirstOrDefaultAsync(s => s.Id == stepId && s.RecipeId == id);

        if (step == null)
        {
            return NotFound();
        }

        // Check if an active, non-expired candidate exists
        var activeDraft = step.AiImageDrafts
            .FirstOrDefault(d => d.State == AiDraftState.Generated && d.ExpiresUtc > DateTime.UtcNow);
        if (activeDraft != null)
        {
            ErrorMessage = "Bước này đã có một ảnh nháp AI đang chờ duyệt. Vui lòng chấp nhận hoặc bỏ ảnh hiện tại trước khi tạo mới.";
            return RedirectToPage(new { id });
        }

        var prompt = _promptBuilder.BuildPrompt(step.Recipe.Name, step.SortOrder, step.Instruction, userBrief);

        GeneratedImage generated;
        try
        {
            generated = await _aiGenerator.GenerateAsync(prompt);
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
            RecipeStepId = stepId,
            PromptSnapshot = prompt,
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
            .Include(d => d.RecipeStep)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeStep.RecipeId == id);

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

    public async Task<IActionResult> OnPostRegenerateAiImageAsync(int id, Guid draftId, string? userBrief)
    {
        if (_aiGenerator == null || _draftStore == null)
        {
            ErrorMessage = "Dịch vụ AI chưa được cấu hình.";
            return RedirectToPage(new { id });
        }

        var draft = await _db.AiImageDrafts
            .Include(d => d.RecipeStep)
                .ThenInclude(s => s.Recipe)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeStep.RecipeId == id);

        if (draft == null)
        {
            return NotFound();
        }

        // Clean up previous draft
        _draftStore.DeleteDraft(draft.TemporaryFileName);
        draft.State = AiDraftState.Discarded;

        var step = draft.RecipeStep;
        var prompt = _promptBuilder.BuildPrompt(step.Recipe.Name, step.SortOrder, step.Instruction, userBrief);

        GeneratedImage generated;
        try
        {
            generated = await _aiGenerator.GenerateAsync(prompt);
        }
        catch (Exception ex)
        {
            await _db.SaveChangesAsync();
            ErrorMessage = $"Tạo lại ảnh AI thất bại: {ex.Message}";
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
            await _db.SaveChangesAsync();
            ErrorMessage = $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}";
            return RedirectToPage(new { id });
        }

        var newDraft = new AiImageDraft
        {
            Id = newDraftId,
            RecipeStepId = step.Id,
            PromptSnapshot = prompt,
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
            .Include(d => d.RecipeStep)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeStep.RecipeId == id);

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
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeStep.RecipeId == id);

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

            var step = draft.RecipeStep;
            step.MediaAsset = mediaAsset;
            draft.State = AiDraftState.Accepted;

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            _draftStore.DeleteDraft(draft.TemporaryFileName);
            SuccessMessage = "Đã chấp nhận và gắn ảnh minh họa AI vào bước công thức thành công.";
        }
        catch
        {
            await tx.RollbackAsync();

            // Compensation: delete remote uploaded image
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

    private async Task<Recipe?> LoadRecipeGraphAsync(int id)
    {
        return await _db.Recipes
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
