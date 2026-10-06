using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Recipes;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class RecipeFinalImageLifecycleTests : IDisposable
{
    private readonly RecipeDbContext _db;
    private readonly FakeStorageService _storage = new();
    private readonly FakeDraftStore _draftStore = new();
    private readonly FakeAiGenerator _aiGenerator = new();
    private readonly FakeAiPromptTranslator _translator = new();
    private readonly ImageValidator _validator = new();

    private static readonly byte[] ValidJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public RecipeFinalImageLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite($"Data Source=file:final_image_test_{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        _db = new RecipeDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _aiGenerator.BytesToReturn = ValidJpeg;
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task GenerateFinalAiImage_creates_draft_with_final_product_target_and_selected_preset()
    {
        var recipe = new Recipe { Name = "Matcha Latte Đậm Vị" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, new AiImagePromptBuilder(), _translator);

        var result = await model.OnPostGenerateFinalAiImageAsync(recipe.Id, AspectRatioPreset.WideLandscape16x9, "Ly trà mát lạnh");
        Assert.IsType<RedirectToPageResult>(result);

        var draft = await _db.AiImageDrafts.FirstOrDefaultAsync(d => d.RecipeId == recipe.Id);
        Assert.NotNull(draft);
        Assert.Equal(AiDraftTargetKind.FinalProduct, draft.TargetKind);
        Assert.Null(draft.RecipeStepId);
        Assert.Equal(AspectRatioPreset.WideLandscape16x9, draft.AspectRatioPreset);
        Assert.Equal("Ly trà mát lạnh", draft.UserBrief);
        Assert.Equal(AiDraftState.Generated, draft.State);
    }

    [Fact]
    public async Task AcceptAiImage_assigns_final_draft_to_recipe_final_media_asset()
    {
        var recipe = new Recipe { Name = "Trà Đào Cam Sả" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var draftId = Guid.NewGuid();
        var tempFile = await _draftStore.SaveDraftAsync(draftId, ValidJpeg, ".jpg");

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeId = recipe.Id,
            RecipeStepId = null,
            TargetKind = AiDraftTargetKind.FinalProduct,
            AspectRatioPreset = AspectRatioPreset.WideLandscape16x9,
            PromptSnapshot = "Finished beverage photo",
            TemporaryFileName = tempFile,
            State = AiDraftState.Generated,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(30)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, new AiImagePromptBuilder(), _translator);
        var result = await model.OnPostAcceptAiImageAsync(recipe.Id, draftId);
        Assert.IsType<RedirectToPageResult>(result);

        var updatedRecipe = await _db.Recipes.Include(r => r.FinalMediaAsset).FirstOrDefaultAsync(r => r.Id == recipe.Id);
        Assert.NotNull(updatedRecipe);
        Assert.NotNull(updatedRecipe.FinalMediaAsset);
        Assert.Equal(MediaSourceType.AiIllustration, updatedRecipe.FinalMediaAsset.SourceType);
        Assert.Equal(AspectRatioPreset.WideLandscape16x9, updatedRecipe.FinalImageAspectRatioPreset);

        var updatedDraft = await _db.AiImageDrafts.FindAsync(draftId);
        Assert.NotNull(updatedDraft);
        Assert.Equal(AiDraftState.Accepted, updatedDraft.State);
    }

    [Fact]
    public async Task RemoveFinalImage_unlinks_recipe_final_media_asset_id()
    {
        var asset = new MediaAsset
        {
            StorageProvider = "Cloudinary",
            ProviderPublicId = "test_final_asset",
            DeliveryUrl = "https://example.com/final.jpg",
            SourceType = MediaSourceType.Real,
            State = MediaAssetState.Active
        };
        _db.MediaAssets.Add(asset);

        var recipe = new Recipe
        {
            Name = "Trà Vải",
            FinalMediaAsset = asset
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator);
        var result = await model.OnPostRemoveFinalImageAsync(recipe.Id);
        Assert.IsType<RedirectToPageResult>(result);

        var updatedRecipe = await _db.Recipes.FindAsync(recipe.Id);
        Assert.NotNull(updatedRecipe);
        Assert.Null(updatedRecipe.FinalMediaAssetId);
    }

    private sealed class FakeStorageService : IImageStorageService
    {
        public Task<StoredImage> UploadAsync(Stream image, ValidatedImageInfo info, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StoredImage(
                ProviderPublicId: $"pub_{Guid.NewGuid():N}",
                DeliveryUrl: "https://example.com/uploaded.jpg",
                ByteSize: info.ByteSize,
                MimeType: info.MimeType,
                OriginalFileName: info.OriginalFileName
            ));
        }

        public Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void DeleteFile(string? fileName) { }
    }

    private sealed class FakeDraftStore : IAiDraftFileStore
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public Task<string> SaveDraftAsync(Guid draftId, byte[] imageBytes, string extension, CancellationToken cancellationToken = default)
        {
            var fileName = $"{draftId:N}{extension}";
            _files[fileName] = imageBytes;
            return Task.FromResult(fileName);
        }

        public Task<byte[]> ReadDraftAsync(string fileName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_files[fileName]);
        }

        public bool DeleteDraft(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            return _files.Remove(fileName);
        }

        public string GetSafePath(string fileName) => fileName;
    }

    private sealed class FakeAiGenerator : IAiImageGenerator
    {
        public byte[] BytesToReturn { get; set; } = [];

        public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeneratedImage(BytesToReturn, "image/jpeg", "@cf/black-forest-labs/flux-2-klein-4b"));
        }

        public Task<GeneratedImage> GenerateAsync(AiImageGenerationRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeneratedImage(BytesToReturn, "image/jpeg", "@cf/black-forest-labs/flux-2-klein-4b"));
        }
    }

    private sealed class FakeAiPromptTranslator : IAiPromptTranslator
    {
        public Task<string> TranslateVietnameseToEnglishAsync(string vietnamesePrompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Translated English beverage prompt");
        }
    }
}
