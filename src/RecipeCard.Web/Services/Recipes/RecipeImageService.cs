using System.IO;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Services;

public record RecipeImageOperationResult(
    bool Success,
    string? ErrorMessage = null,
    string? SuccessMessage = null,
    Guid? DraftId = null,
    bool NotFound = false);

public record DraftImageContentResult(
    bool Success,
    byte[]? Bytes = null,
    string? MimeType = null,
    bool NotFound = false);

public interface IRecipeImageService
{
    Task<RecipeImageOperationResult> GenerateFinalProductDraftAsync(
        int recipeId,
        AspectRatioPreset? preset,
        string? userBrief,
        AiImageProvider? provider = null,
        CancellationToken cancellationToken = default);

    Task<RecipeImageOperationResult> GenerateStepDraftAsync(
        int recipeId,
        int stepId,
        AspectRatioPreset? preset,
        string? userBrief,
        AiImageProvider? provider = null,
        CancellationToken cancellationToken = default);

    Task<RecipeImageOperationResult> RegenerateDraftAsync(
        int recipeId,
        Guid draftId,
        AspectRatioPreset? preset,
        string? userBrief,
        AiImageProvider? provider = null,
        CancellationToken cancellationToken = default);
    Task<RecipeImageOperationResult> DiscardDraftAsync(
        int recipeId,
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<RecipeImageOperationResult> AcceptDraftAsync(
        int recipeId,
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<DraftImageContentResult> GetDraftImageAsync(
        int recipeId,
        Guid draftId,
        CancellationToken cancellationToken = default);
}

public class RecipeImageService : IRecipeImageService
{
    private readonly RecipeDbContext _db;
    private readonly IAiImageGeneratorRouter? _aiRouter;
    private readonly IAiPromptTranslator? _promptTranslator;
    private readonly IAiImagePromptBuilder _promptBuilder;
    private readonly IAiDraftFileStore? _draftStore;
    private readonly IImageStorageService _imageStorage;
    private readonly IImageValidator _validator;

    public RecipeImageService(
        RecipeDbContext db,
        IAiImagePromptBuilder promptBuilder,
        IImageStorageService imageStorage,
        IImageValidator? validator = null,
        IAiImageGeneratorRouter? aiRouter = null,
        IAiPromptTranslator? promptTranslator = null,
        IAiDraftFileStore? draftStore = null,
        IAiImageGenerator? legacyAiGenerator = null)
    {
        _db = db;
        _promptBuilder = promptBuilder;
        _imageStorage = imageStorage;
        _validator = validator ?? new ImageValidator();
        _aiRouter = aiRouter ?? (legacyAiGenerator != null ? new AiImageGeneratorRouter(legacyAiGenerator) : null);
        _promptTranslator = promptTranslator;
        _draftStore = draftStore;
    }

    public async Task<RecipeImageOperationResult> GenerateFinalProductDraftAsync(
        int recipeId,
        AspectRatioPreset? preset,
        string? userBrief,
        AiImageProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        if (_aiRouter == null || _draftStore == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ AI chưa được cấu hình.");
        }

        if (_promptTranslator == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ dịch prompt AI chưa được cấu hình.");
        }

        var recipe = await _db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);

        if (recipe == null)
        {
            return new RecipeImageOperationResult(false, NotFound: true);
        }

        var activeDraft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.RecipeId == recipeId
                && d.TargetKind == AiDraftTargetKind.FinalProduct
                && d.State == AiDraftState.Generated
                && d.ExpiresUtc > DateTime.UtcNow, cancellationToken);

        if (activeDraft != null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Công thức đã có một ảnh thành phẩm nháp AI đang chờ duyệt. Vui lòng chấp nhận hoặc bỏ ảnh trước khi tạo mới.");
        }

        var targetPreset = preset ?? recipe.FinalImageAspectRatioPreset;
        recipe.FinalImageAspectRatioPreset = targetPreset;

        string sourcePrompt;
        try
        {
            var ings = recipe.Ingredients.Select(ri => (ri.Ingredient.Name, ri.Quantity, ri.Ingredient.DefaultUnit));
            var steps = recipe.Steps.OrderBy(s => s.SortOrder).Select(s => (s.SortOrder, s.Instruction));
            sourcePrompt = _promptBuilder.BuildFinalProductSourcePrompt(recipe.Name, ings, steps, recipe.GeneralNote, userBrief);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Dựng prompt thất bại: {ex.Message}");
        }

        string translated;
        try
        {
            translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Dịch mô tả cho AI thất bại: {ex.Message}");
        }

        string fullPrompt;
        try
        {
            fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.FinalProduct, translated);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: ex.Message);
        }

        var (w, h) = targetPreset.ToDimensions();
        var request = new AiImageGenerationRequest(fullPrompt, targetPreset, w, h);

        GeneratedImage generated;
        try
        {
            generated = await _aiRouter.GenerateAsync(request, provider, cancellationToken);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Tạo ảnh AI thất bại: {ex.Message}");
        }

        var draftId = Guid.NewGuid();
        string tempFileName;
        try
        {
            tempFileName = await _draftStore.SaveDraftAsync(draftId, generated.ImageBytes, ".jpg");
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}");
        }

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeId = recipeId,
            RecipeStepId = null,
            TargetKind = AiDraftTargetKind.FinalProduct,
            AspectRatioPreset = targetPreset,
            PromptSnapshot = fullPrompt,
            UserBrief = string.IsNullOrWhiteSpace(userBrief) ? null : userBrief.Trim(),
            Model = generated.Model,
            Provider = provider ?? _aiRouter.Options.DefaultProvider,
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };

        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync(cancellationToken);

        return new RecipeImageOperationResult(
            true,
            SuccessMessage: "Đã tạo ảnh nháp AI thành phẩm thành công. Hãy xem lại và chọn Chấp nhận hoặc Bỏ ảnh.",
            DraftId: draftId);
    }
    public async Task<RecipeImageOperationResult> GenerateStepDraftAsync(
        int recipeId,
        int stepId,
        AspectRatioPreset? preset,
        string? userBrief,
        AiImageProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        if (_aiRouter == null || _draftStore == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ AI chưa được cấu hình.");
        }

        if (_promptTranslator == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ dịch prompt AI chưa được cấu hình.");
        }

        var recipe = await _db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Steps).ThenInclude(s => s.AiImageDrafts)
            .FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);

        if (recipe == null)
        {
            return new RecipeImageOperationResult(false, NotFound: true);
        }

        var step = recipe.Steps.FirstOrDefault(s => s.Id == stepId);
        if (step == null)
        {
            return new RecipeImageOperationResult(false, NotFound: true);
        }

        var activeDraft = step.AiImageDrafts
            .FirstOrDefault(d => d.State == AiDraftState.Generated && d.ExpiresUtc > DateTime.UtcNow);
        if (activeDraft != null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Bước này đã có một ảnh nháp AI đang chờ duyệt. Vui lòng chấp nhận hoặc bỏ ảnh hiện tại trước khi tạo mới.");
        }

        var targetPreset = preset ?? step.ImageAspectRatioPreset;
        step.ImageAspectRatioPreset = targetPreset;

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
            return new RecipeImageOperationResult(false, ErrorMessage: $"Dựng prompt thất bại: {ex.Message}");
        }

        string translated;
        try
        {
            translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Dịch mô tả cho AI thất bại: {ex.Message}");
        }

        string fullPrompt;
        try
        {
            fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.StepInstruction, translated);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: ex.Message);
        }

        var (w, h) = targetPreset.ToDimensions();
        var request = new AiImageGenerationRequest(fullPrompt, targetPreset, w, h);

        GeneratedImage generated;
        try
        {
            generated = await _aiRouter.GenerateAsync(request, provider, cancellationToken);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Tạo ảnh AI thất bại: {ex.Message}");
        }
        var draftId = Guid.NewGuid();
        string tempFileName;
        try
        {
            tempFileName = await _draftStore.SaveDraftAsync(draftId, generated.ImageBytes, ".jpg");
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}");
        }

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeId = recipeId,
            RecipeStepId = stepId,
            TargetKind = AiDraftTargetKind.StepInstruction,
            AspectRatioPreset = targetPreset,
            PromptSnapshot = fullPrompt,
            UserBrief = string.IsNullOrWhiteSpace(userBrief) ? null : userBrief.Trim(),
            Model = generated.Model,
            Provider = provider ?? _aiRouter.Options.DefaultProvider,
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync(cancellationToken);

        return new RecipeImageOperationResult(
            true,
            SuccessMessage: "Đã tạo ảnh nháp AI thành công. Hãy xem lại và chọn Chấp nhận hoặc Bỏ ảnh.",
            DraftId: draftId);
    }

    public async Task<RecipeImageOperationResult> RegenerateDraftAsync(
        int recipeId,
        Guid draftId,
        AspectRatioPreset? preset,
        string? userBrief,
        AiImageProvider? provider = null,
        CancellationToken cancellationToken = default)
    {
        if (_aiRouter == null || _draftStore == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ AI chưa được cấu hình.");
        }

        if (_promptTranslator == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ dịch prompt AI chưa được cấu hình.");
        }

        var currentDraft = await _db.AiImageDrafts
            .Include(d => d.RecipeStep)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == recipeId, cancellationToken);

        if (currentDraft == null || currentDraft.State != AiDraftState.Generated)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Bản nháp AI không tồn tại hoặc đã được xử lý trước đó.");
        }

        if (currentDraft.ExpiresUtc <= DateTime.UtcNow)
        {
            currentDraft.State = AiDraftState.Expired;
            _draftStore.DeleteDraft(currentDraft.TemporaryFileName);
            await _db.SaveChangesAsync(cancellationToken);
            return new RecipeImageOperationResult(false, ErrorMessage: "Ảnh nháp AI đã hết hạn. Vui lòng tạo lại ảnh mới.");
        }

        var targetPreset = preset ?? currentDraft.AspectRatioPreset;
        string fullPrompt;

        if (currentDraft.TargetKind == AiDraftTargetKind.FinalProduct)
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);

            if (recipe == null)
            {
                return new RecipeImageOperationResult(false, NotFound: true);
            }

            var ings = recipe.Ingredients.Select(ri => (ri.Ingredient.Name, ri.Quantity, ri.Ingredient.DefaultUnit));
            var steps = recipe.Steps.OrderBy(s => s.SortOrder).Select(s => (s.SortOrder, s.Instruction));
            var sourcePrompt = _promptBuilder.BuildFinalProductSourcePrompt(recipe.Name, ings, steps, recipe.GeneralNote, userBrief);
            string translated;
            try
            {
                translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, cancellationToken);
                fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.FinalProduct, translated);
            }
            catch (Exception ex)
            {
                return new RecipeImageOperationResult(false, ErrorMessage: $"Dịch mô tả cho AI thất bại: {ex.Message}");
            }
            recipe.FinalImageAspectRatioPreset = targetPreset;
        }
        else
        {
            var recipe = await _db.Recipes
                .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.Id == recipeId, cancellationToken);

            if (recipe == null)
            {
                return new RecipeImageOperationResult(false, NotFound: true);
            }

            var step = recipe.Steps.FirstOrDefault(s => s.Id == currentDraft.RecipeStepId);
            if (step == null)
            {
                return new RecipeImageOperationResult(false, NotFound: true);
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
                translated = await _promptTranslator.TranslateVietnameseToEnglishAsync(sourcePrompt, cancellationToken);
                fullPrompt = _promptBuilder.AttachCanonicalStyle(AiDraftTargetKind.StepInstruction, translated);
            }
            catch (Exception ex)
            {
                return new RecipeImageOperationResult(false, ErrorMessage: $"Dịch mô tả cho AI thất bại: {ex.Message}");
            }
            step.ImageAspectRatioPreset = targetPreset;
        }

        var (w, h) = targetPreset.ToDimensions();
        var request = new AiImageGenerationRequest(fullPrompt, targetPreset, w, h);

        var targetProvider = provider ?? currentDraft.Provider;
        GeneratedImage generated;
        try
        {
            generated = await _aiRouter.GenerateAsync(request, targetProvider, cancellationToken);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Tạo ảnh AI thất bại: {ex.Message}");
        }
        var newDraftId = Guid.NewGuid();
        string tempFileName;
        try
        {
            tempFileName = await _draftStore.SaveDraftAsync(newDraftId, generated.ImageBytes, ".jpg");
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Lỗi lưu ảnh nháp tạm thời: {ex.Message}");
        }

        _draftStore.DeleteDraft(currentDraft.TemporaryFileName);
        currentDraft.State = AiDraftState.Discarded;

        var newDraft = new AiImageDraft
        {
            Id = newDraftId,
            RecipeId = recipeId,
            RecipeStepId = currentDraft.RecipeStepId,
            TargetKind = currentDraft.TargetKind,
            AspectRatioPreset = targetPreset,
            PromptSnapshot = fullPrompt,
            UserBrief = string.IsNullOrWhiteSpace(userBrief) ? null : userBrief.Trim(),
            Model = generated.Model,
            Provider = targetProvider,
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };

        _db.AiImageDrafts.Add(newDraft);
        await _db.SaveChangesAsync(cancellationToken);

        return new RecipeImageOperationResult(
            true,
            SuccessMessage: "Đã tạo lại ảnh nháp AI mới.",
            DraftId: newDraftId);
    }

    public async Task<RecipeImageOperationResult> DiscardDraftAsync(
        int recipeId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        var draft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == recipeId, cancellationToken);

        if (draft != null)
        {
            if (_draftStore != null)
            {
                _draftStore.DeleteDraft(draft.TemporaryFileName);
            }
            draft.State = AiDraftState.Discarded;
            await _db.SaveChangesAsync(cancellationToken);
            return new RecipeImageOperationResult(true, SuccessMessage: "Đã bỏ ảnh nháp AI.");
        }

        return new RecipeImageOperationResult(true);
    }

    public async Task<RecipeImageOperationResult> AcceptDraftAsync(
        int recipeId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        if (_draftStore == null)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Dịch vụ lưu trữ nháp chưa sẵn sàng.");
        }

        var draft = await _db.AiImageDrafts
            .Include(d => d.RecipeStep)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == recipeId, cancellationToken);

        if (draft == null || draft.State != AiDraftState.Generated)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: "Bản nháp AI không tồn tại hoặc đã được xử lý trước đó.");
        }

        if (draft.ExpiresUtc <= DateTime.UtcNow)
        {
            draft.State = AiDraftState.Expired;
            _draftStore.DeleteDraft(draft.TemporaryFileName);
            await _db.SaveChangesAsync(cancellationToken);
            return new RecipeImageOperationResult(false, ErrorMessage: "Ảnh nháp AI đã hết hạn. Vui lòng tạo lại ảnh mới.");
        }

        byte[] draftBytes;
        try
        {
            draftBytes = await _draftStore.ReadDraftAsync(draft.TemporaryFileName);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Không thể đọc file ảnh nháp: {ex.Message}");
        }

        ValidatedImageInfo info;
        try
        {
            info = _validator.ValidateBytes(draftBytes, draft.TemporaryFileName);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Ảnh nháp không đạt chuẩn hợp lệ: {ex.Message}");
        }

        StoredImage stored;
        try
        {
            using var stream = new MemoryStream(draftBytes);
            stored = await _imageStorage.UploadAsync(stream, info);
        }
        catch (Exception ex)
        {
            return new RecipeImageOperationResult(false, ErrorMessage: $"Tải ảnh lên máy chủ lưu trữ thất bại: {ex.Message}");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
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
                var recipe = await _db.Recipes.FindAsync([recipeId], cancellationToken);
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

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _draftStore.DeleteDraft(draft.TemporaryFileName);
            var successMessage = draft.TargetKind == AiDraftTargetKind.FinalProduct
                ? "Đã chấp nhận và gắn ảnh AI làm ảnh thành phẩm đại diện cho công thức."
                : "Đã chấp nhận và gắn ảnh minh họa AI vào bước công thức thành công.";

            return new RecipeImageOperationResult(true, SuccessMessage: successMessage);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            try
            {
                await _imageStorage.DeleteAsync(stored.ProviderPublicId);
            }
            catch
            {
            }
            throw;
        }
    }

    public async Task<DraftImageContentResult> GetDraftImageAsync(
        int recipeId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        if (_draftStore == null)
        {
            return new DraftImageContentResult(false, NotFound: true);
        }

        var draft = await _db.AiImageDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.RecipeId == recipeId, cancellationToken);

        if (draft == null || draft.State != AiDraftState.Generated || draft.ExpiresUtc <= DateTime.UtcNow)
        {
            return new DraftImageContentResult(false, NotFound: true);
        }

        try
        {
            var bytes = await _draftStore.ReadDraftAsync(draft.TemporaryFileName);
            var info = _validator.ValidateBytes(bytes, draft.TemporaryFileName);
            return new DraftImageContentResult(true, Bytes: bytes, MimeType: info.MimeType);
        }
        catch
        {
            return new DraftImageContentResult(false, NotFound: true);
        }
    }
}
