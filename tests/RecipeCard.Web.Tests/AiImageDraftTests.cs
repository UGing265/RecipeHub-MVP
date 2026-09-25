using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiImageDraftTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecipeDbContext _db;
    private readonly string _testContentRoot;
    private readonly AiDraftFileStore _store;

    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public AiImageDraftTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RecipeDbContext(options);
        _db.Database.EnsureCreated();

        _testContentRoot = Path.Combine(Path.GetTempPath(), "draft_store_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testContentRoot);

        var env = new FakeWebHostEnvironment { ContentRootPath = _testContentRoot };
        _store = new AiDraftFileStore(env);
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
    public async Task AiDraftFileStore_saves_reads_and_deletes_draft_safely()
    {
        var draftId = Guid.NewGuid();
        var fileName = await _store.SaveDraftAsync(draftId, ValidJpegBytes, ".jpg");

        Assert.Equal($"{draftId:N}.jpg", fileName);

        var readBytes = await _store.ReadDraftAsync(fileName);
        Assert.Equal(ValidJpegBytes, readBytes);

        var deleted = _store.DeleteDraft(fileName);
        Assert.True(deleted);

        await Assert.ThrowsAsync<FileNotFoundException>(() => _store.ReadDraftAsync(fileName));
    }

    [Fact]
    public void AiDraftFileStore_prevents_path_traversal()
    {
        Assert.Throws<InvalidOperationException>(() => _store.GetSafePath("../secret.txt"));
        Assert.Throws<InvalidOperationException>(() => _store.GetSafePath(@"..\secret.txt"));
    }

    [Fact]
    public async Task AiImageDraft_cascades_delete_when_step_deleted()
    {
        var recipe = new Recipe { Name = "Draft Cascade Test" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Step with draft"
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var draft = new AiImageDraft
        {
            RecipeStepId = step.Id,
            PromptSnapshot = "Test prompt",
            TemporaryFileName = "temp.jpg",
            ExpiresUtc = DateTime.UtcNow.AddHours(2)
        };
        _db.AiImageDrafts.Add(draft);
        await _db.SaveChangesAsync();

        // Remove step
        _db.RecipeSteps.Remove(step);
        await _db.SaveChangesAsync();

        // Draft should be cascade-deleted
        var foundDraft = await _db.AiImageDrafts.FindAsync(draft.Id);
        Assert.Null(foundDraft);
    }

    [Fact]
    public async Task MediaAsset_enforces_unique_provider_public_id()
    {
        var asset1 = new MediaAsset
        {
            ProviderPublicId = "unique-pub-id-123",
            DeliveryUrl = "https://res.cloudinary.com/test/image1.jpg",
            StorageProvider = "Cloudinary",
            MimeType = "image/jpeg"
        };
        _db.MediaAssets.Add(asset1);
        await _db.SaveChangesAsync();

        var asset2 = new MediaAsset
        {
            ProviderPublicId = "unique-pub-id-123",
            DeliveryUrl = "https://res.cloudinary.com/test/image2.jpg",
            StorageProvider = "Cloudinary",
            MimeType = "image/jpeg"
        };
        _db.MediaAssets.Add(asset2);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }
}
