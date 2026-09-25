using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Recipes;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiImageCandidateTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecipeDbContext _db;
    private readonly FakeStorageService _storage;
    private readonly FakeAiGenerator _aiGenerator;
    private readonly AiDraftFileStore _draftStore;
    private readonly ImageValidator _validator;
    private readonly AiImagePromptBuilder _promptBuilder;
    private readonly string _testContentRoot;

    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public AiImageCandidateTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RecipeDbContext(options);
        _db.Database.EnsureCreated();

        _testContentRoot = Path.Combine(Path.GetTempPath(), "candidate_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testContentRoot);

        var env = new FakeWebHostEnvironment { ContentRootPath = _testContentRoot };
        _draftStore = new AiDraftFileStore(env);
        _storage = new FakeStorageService();
        _aiGenerator = new FakeAiGenerator { BytesToReturn = ValidJpegBytes };
        _validator = new ImageValidator();
        _promptBuilder = new AiImagePromptBuilder();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();

        if (Directory.Exists(_testContentRoot))
        {
            try
            {
                Directory.Delete(_testContentRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void PromptBuilder_creates_canonical_prompt()
    {
        var prompt = _promptBuilder.BuildPrompt("Trà Ô Long Sữa", 2, "Khuấy đều hỗn hợp", "góc nhìn từ trên xuống");

        Assert.Contains("Trà Ô Long Sữa", prompt);
        Assert.Contains("Step 2", prompt);
        Assert.Contains("Khuấy đều hỗn hợp", prompt);
        Assert.Contains("góc nhìn từ trên xuống", prompt);
        Assert.Contains("Clear beverage preparation photo", prompt);
    }

    [Fact]
    public void PromptBuilder_truncates_oversized_brief()
    {
        var oversized = new string('a', 600);
        var prompt = _promptBuilder.BuildPrompt("Trà Ô Long", 1, "Rót nước", oversized);

        Assert.DoesNotContain(oversized, prompt);
        Assert.Contains(new string('a', 500), prompt);
    }

    [Fact]
    public async Task GenerateAiImage_creates_draft_without_uploading_to_storage()
    {
        var recipe = new Recipe { Name = "Công thức AI" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Bước cần ảnh AI"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder);

        var result = await model.OnPostGenerateAiImageAsync(recipe.Id, step.Id, "ly thủy tinh");
        Assert.IsType<RedirectToPageResult>(result);

        var draft = await _db.AiImageDrafts.FirstOrDefaultAsync(d => d.RecipeStepId == step.Id);
        Assert.NotNull(draft);
        Assert.Equal(AiDraftState.Generated, draft.State);
        Assert.Equal("ly thủy tinh", draft.UserBrief);

        // File must exist in draft file store
        var fileBytes = await _draftStore.ReadDraftAsync(draft.TemporaryFileName);
        Assert.Equal(ValidJpegBytes, fileBytes);

        // Must NOT upload to storage prematurely!
        Assert.Empty(_storage.UploadedPublicIds);
    }

    [Fact]
    public async Task GenerateAiImage_rejects_second_generation_if_active_draft_exists()
    {
        var recipe = new Recipe { Name = "Công thức AI 2" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Bước cần ảnh AI"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var draft = new AiImageDraft
        {
            RecipeStepId = step.Id,
            PromptSnapshot = "Prompt 1",
            TemporaryFileName = "draft1.jpg",
            State = AiDraftState.Generated,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder);
        await model.OnPostGenerateAiImageAsync(recipe.Id, step.Id, "brief");

        Assert.Contains("đã có một ảnh nháp AI đang chờ duyệt", model.ErrorMessage);
    }

    [Fact]
    public async Task DiscardAiImage_marks_draft_discarded_and_deletes_file()
    {
        var recipe = new Recipe { Name = "Công thức Discard" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Bước discard"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var draftId = Guid.NewGuid();
        var tempFileName = await _draftStore.SaveDraftAsync(draftId, ValidJpegBytes, ".jpg");

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeStepId = step.Id,
            PromptSnapshot = "Prompt",
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder);
        await model.OnPostDiscardAiImageAsync(recipe.Id, draft.Id);

        var updatedDraft = await _db.AiImageDrafts.FindAsync(draft.Id);
        Assert.NotNull(updatedDraft);
        Assert.Equal(AiDraftState.Discarded, updatedDraft.State);

        // File must be deleted from store
        await Assert.ThrowsAsync<FileNotFoundException>(() => _draftStore.ReadDraftAsync(tempFileName));
    }

    [Fact]
    public async Task AcceptAiImage_uploads_to_storage_and_links_asset_to_step()
    {
        var recipe = new Recipe { Name = "Công thức Accept" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Bước accept"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var draftId = Guid.NewGuid();
        var tempFileName = await _draftStore.SaveDraftAsync(draftId, ValidJpegBytes, ".jpg");

        var draft = new AiImageDraft
        {
            Id = draftId,
            RecipeStepId = step.Id,
            PromptSnapshot = "Prompt to accept",
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder);
        var result = await model.OnPostAcceptAiImageAsync(recipe.Id, draft.Id);

        Assert.IsType<RedirectToPageResult>(result);

        // 1. Uploaded exactly once to storage
        Assert.Single(_storage.UploadedPublicIds);

        // 2. Draft is Accepted
        var updatedDraft = await _db.AiImageDrafts.FindAsync(draft.Id);
        Assert.NotNull(updatedDraft);
        Assert.Equal(AiDraftState.Accepted, updatedDraft.State);

        // 3. Step has MediaAsset with AiIllustration source
        var updatedStep = await _db.RecipeSteps.Include(s => s.MediaAsset).FirstAsync(s => s.Id == step.Id);
        Assert.NotNull(updatedStep.MediaAsset);
        Assert.Equal(MediaSourceType.AiIllustration, updatedStep.MediaAsset.SourceType);
        Assert.Equal(draft.Id, updatedStep.MediaAsset.AiImageDraftId);

        // 4. Staged temporary file is deleted after acceptance
        await Assert.ThrowsAsync<FileNotFoundException>(() => _draftStore.ReadDraftAsync(tempFileName));
    }

    private sealed class FakeAiGenerator : IAiImageGenerator
    {
        public byte[] BytesToReturn { get; set; } = [];

        public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeneratedImage(
                ImageBytes: BytesToReturn,
                MimeType: "image/jpeg",
                Model: "@cf/black-forest-labs/flux-1-schnell"
            ));
        }
    }

    private sealed class FakeStorageService : IImageStorageService
    {
        public List<string> UploadedPublicIds { get; } = [];

        public Task<StoredImage> UploadAsync(Stream image, ValidatedImageInfo info, CancellationToken cancellationToken = default)
        {
            var publicId = $"pub_{Guid.NewGuid():N}";
            UploadedPublicIds.Add(publicId);
            return Task.FromResult(new StoredImage(
                ProviderPublicId: publicId,
                DeliveryUrl: $"https://res.cloudinary.com/demo/image/upload/{publicId}.jpg",
                ByteSize: info.ByteSize,
                MimeType: info.MimeType,
                OriginalFileName: info.OriginalFileName
            ));
        }

        public Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
