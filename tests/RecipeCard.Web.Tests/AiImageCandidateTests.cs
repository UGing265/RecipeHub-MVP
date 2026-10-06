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
    private readonly FakeAiPromptTranslator _translator;
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
        _translator = new FakeAiPromptTranslator { TranslationToReturn = "Beverage recipe: AI Recipe. Step 1: Step instruction." };
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
    public void PromptBuilder_creates_source_and_final_prompts()
    {
        var source = _promptBuilder.BuildVietnameseSourcePrompt(
            "Matcha sữa dừa", 2, "Đong sữa dừa vào ly có đá", "ly thủy tinh");
        var final = _promptBuilder.BuildFinalImagePrompt(
            "Beverage recipe: Coconut milk matcha. Step 2: Pour coconut milk into a glass with ice.");

        Assert.Contains("Matcha sữa dừa", source);
        Assert.Contains("Bước 2", source);
        Assert.Contains("Đong sữa dừa vào ly có đá", source);
        Assert.Contains("ly thủy tinh", source);
        Assert.DoesNotContain("Clear beverage preparation photo", source);

        Assert.Contains("Beverage recipe: Coconut milk matcha. Step 2: Pour coconut milk into a glass with ice.", final);
        Assert.Contains(AiImagePromptBuilder.DefaultStyleGuideline, final);
        Assert.DoesNotContain("Matcha sữa dừa", final);
    }

    [Fact]
    public void PromptBuilder_truncates_oversized_brief()
    {
        var oversized = new string('a', 600);
        var source = _promptBuilder.BuildVietnameseSourcePrompt("Trà Ô Long", 1, "Rót nước", oversized);

        Assert.DoesNotContain(oversized, source);
        Assert.Contains(new string('a', 500), source);
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
        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder, _translator);
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
            RecipeId = recipe.Id,
            RecipeStepId = step.Id,
            PromptSnapshot = "Prompt 1",
            TemporaryFileName = "draft1.jpg",
            State = AiDraftState.Generated,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();
        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder, _translator);
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
            RecipeId = recipe.Id,
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
            RecipeId = recipe.Id,
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

    [Fact]
    public async Task GenerateAiImage_translates_prompt_and_stores_final_english_in_snapshot()
    {
        var recipe = new Recipe { Name = "Matcha Đá Xay" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 2,
            Instruction = "Xay nhuyễn hỗn hợp matcha với đá"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        _translator.TranslationToReturn = "Blend matcha mixture with ice until smooth.";
        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder, _translator);

        var result = await model.OnPostGenerateAiImageAsync(recipe.Id, step.Id, "ly thủy tinh cao");
        Assert.IsType<RedirectToPageResult>(result);

        // 1. Translator called with Vietnamese source prompt
        Assert.Single(_translator.PromptsReceived);
        var receivedPrompt = _translator.PromptsReceived[0];
        Assert.Contains("Matcha Đá Xay", receivedPrompt);
        Assert.Contains("Bước 2", receivedPrompt);
        Assert.Contains("Xay nhuyễn hỗn hợp matcha với đá", receivedPrompt);
        Assert.Contains("ly thủy tinh cao", receivedPrompt);

        // 2. Generator called with translated English + default style guideline
        Assert.Single(_aiGenerator.PromptsReceived);
        var generatorPrompt = _aiGenerator.PromptsReceived[0];
        Assert.Contains("Blend matcha mixture with ice until smooth.", generatorPrompt);
        Assert.Contains(AiImagePromptBuilder.DefaultStyleGuideline, generatorPrompt);
        Assert.DoesNotContain("Matcha Đá Xay", generatorPrompt);

        // 3. Draft snapshot stores the final English prompt
        var draft = await _db.AiImageDrafts.FirstAsync(d => d.RecipeStepId == step.Id);
        Assert.Equal(generatorPrompt, draft.PromptSnapshot);
    }

    [Fact]
    public async Task RegenerateAiImage_translates_prompt_and_stores_final_english_in_snapshot()
    {
        var recipe = new Recipe { Name = "Cà Phê Muối" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Rót lớp kem muối lên trên cà phê"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var draftId = Guid.NewGuid();
        var tempFileName = await _draftStore.SaveDraftAsync(draftId, ValidJpegBytes, ".jpg");
        var initialDraft = new AiImageDraft
        {
            Id = draftId,
            RecipeId = recipe.Id,
            RecipeStepId = step.Id,
            PromptSnapshot = "Initial English prompt",
            TemporaryFileName = tempFileName,
            State = AiDraftState.Generated,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(20)
        };
        _db.AiImageDrafts.Add(initialDraft);
        await _db.SaveChangesAsync();

        _translator.TranslationToReturn = "Pour salted cream foam over coffee.";
        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder, _translator);

        var result = await model.OnPostRegenerateAiImageAsync(recipe.Id, draftId, "thêm bột ca cao rắc mặt");
        Assert.IsType<RedirectToPageResult>(result);

        // 1. Translator called with new Vietnamese source prompt
        Assert.Single(_translator.PromptsReceived);
        var receivedPrompt = _translator.PromptsReceived[0];
        Assert.Contains("Cà Phê Muối", receivedPrompt);
        Assert.Contains("Bước 1", receivedPrompt);
        Assert.Contains("Rót lớp kem muối lên trên cà phê", receivedPrompt);
        Assert.Contains("thêm bột ca cao rắc mặt", receivedPrompt);

        // 2. Generator called with translated English + default style guideline
        Assert.Single(_aiGenerator.PromptsReceived);
        var generatorPrompt = _aiGenerator.PromptsReceived[0];
        Assert.Contains("Pour salted cream foam over coffee.", generatorPrompt);
        Assert.Contains(AiImagePromptBuilder.DefaultStyleGuideline, generatorPrompt);

        // 3. New draft has updated snapshot
        var newDraft = await _db.AiImageDrafts.FirstAsync(d => d.RecipeStepId == step.Id && d.State == AiDraftState.Generated);
        Assert.Equal(generatorPrompt, newDraft.PromptSnapshot);
    }

    [Fact]
    public async Task GenerateAiImage_translation_failure_creates_no_draft_and_sets_error()
    {
        var recipe = new Recipe { Name = "Trà Đào" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Thả đào ngâm vào ly"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        _translator.ExceptionToThrow = new InvalidOperationException("Model translation failed");
        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder, _translator);

        var result = await model.OnPostGenerateAiImageAsync(recipe.Id, step.Id, "trang trí lá bạc hà");
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Contains("Dịch mô tả cho AI thất bại: Model translation failed", model.ErrorMessage);
        Assert.Empty(await _db.AiImageDrafts.ToListAsync());
        Assert.Empty(_storage.UploadedPublicIds);
        Assert.Empty(_aiGenerator.PromptsReceived);
    }

    [Fact]
    public async Task GenerateAiImage_fails_early_when_translator_not_configured()
    {
        var recipe = new Recipe { Name = "Trà Đào" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Thả đào ngâm vào ly"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var model = new EditModel(_db, _storage, _validator, _aiGenerator, _draftStore, _promptBuilder, promptTranslator: null);

        var result = await model.OnPostGenerateAiImageAsync(recipe.Id, step.Id, null);
        Assert.IsType<RedirectToPageResult>(result);

        Assert.Equal("Dịch vụ dịch prompt AI chưa được cấu hình.", model.ErrorMessage);
        Assert.Empty(await _db.AiImageDrafts.ToListAsync());
        Assert.Empty(_aiGenerator.PromptsReceived);
    }

    private sealed class FakeAiGenerator : IAiImageGenerator
    {
        public byte[] BytesToReturn { get; set; } = [];
        public List<string> PromptsReceived { get; } = [];

        public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            PromptsReceived.Add(prompt);
            return Task.FromResult(new GeneratedImage(
                ImageBytes: BytesToReturn,
                MimeType: "image/jpeg",
                Model: "@cf/black-forest-labs/flux-2-klein-4b"
            ));
        }

        public Task<GeneratedImage> GenerateAsync(AiImageGenerationRequest request, CancellationToken cancellationToken = default)
        {
            return GenerateAsync(request.Prompt, cancellationToken);
        }
    }

    private sealed class FakeAiPromptTranslator : IAiPromptTranslator
    {
        public string TranslationToReturn { get; set; } = "Translated English prompt";
        public List<string> PromptsReceived { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task<string> TranslateVietnameseToEnglishAsync(string vietnamesePrompt, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            PromptsReceived.Add(vietnamesePrompt);
            return Task.FromResult(TranslationToReturn);
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
