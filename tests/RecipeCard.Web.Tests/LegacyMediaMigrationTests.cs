using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class LegacyMediaMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecipeDbContext _db;
    private readonly string _testWebRoot;
    private readonly string _testUploadsFolder;
    private readonly FakeStorageService _storage;
    private readonly ImageValidator _validator;
    private readonly FakeWebHostEnvironment _env;

    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public LegacyMediaMigrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RecipeDbContext(options);
        _db.Database.EnsureCreated();

        _testWebRoot = Path.Combine(Path.GetTempPath(), "recipe_migration_test_" + Guid.NewGuid().ToString("N"));
        _testUploadsFolder = Path.Combine(_testWebRoot, "uploads", "steps");
        Directory.CreateDirectory(_testUploadsFolder);

        _env = new FakeWebHostEnvironment { WebRootPath = _testWebRoot };
        _storage = new FakeStorageService();
        _validator = new ImageValidator();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();

        if (Directory.Exists(_testWebRoot))
        {
            try
            {
                Directory.Delete(_testWebRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task MigrateAsync_converts_existing_valid_files()
    {
        var validFile = "step1.png";
        await File.WriteAllBytesAsync(Path.Combine(_testUploadsFolder, validFile), ValidPngBytes);

        var recipe = new Recipe { Name = "Test Recipe" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Step with legacy image",
            ImageFileName = validFile
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var migrator = new LegacyStepImageMigrationService(_db, _storage, _validator, _env);
        var report = await migrator.MigrateAsync();

        Assert.Equal(1, report.TotalScanned);
        Assert.Equal(1, report.Converted);
        Assert.Equal(0, report.Missing);
        Assert.Equal(0, report.Invalid);

        var updatedStep = await _db.RecipeSteps.Include(s => s.MediaAsset).FirstAsync(s => s.Id == step.Id);
        Assert.NotNull(updatedStep.MediaAssetId);
        Assert.NotNull(updatedStep.MediaAsset);
        Assert.Equal(MediaSourceType.Real, updatedStep.MediaAsset.SourceType);
        Assert.Equal("image/png", updatedStep.MediaAsset.MimeType);
    }

    [Fact]
    public async Task MigrateAsync_reports_missing_and_invalid_files()
    {
        var recipe = new Recipe { Name = "Test Recipe" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        // 1. Missing file
        _db.RecipeSteps.Add(new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Step missing file",
            ImageFileName = "missing.png"
        });

        // 2. Corrupt file
        var corruptFile = "corrupt.png";
        await File.WriteAllBytesAsync(Path.Combine(_testUploadsFolder, corruptFile), "not an image"u8.ToArray());
        _db.RecipeSteps.Add(new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 2,
            Instruction = "Step corrupt file",
            ImageFileName = corruptFile
        });

        // 3. Path traversal file
        _db.RecipeSteps.Add(new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 3,
            Instruction = "Step malicious traversal",
            ImageFileName = "../hack.png"
        });

        await _db.SaveChangesAsync();

        var migrator = new LegacyStepImageMigrationService(_db, _storage, _validator, _env);
        var report = await migrator.MigrateAsync();

        Assert.Equal(3, report.TotalScanned);
        Assert.Equal(0, report.Converted);
        Assert.Equal(1, report.Missing);
        Assert.Equal(2, report.Invalid);
    }

    private sealed class FakeStorageService : IImageStorageService
    {
        public Task<StoredImage> UploadAsync(Stream image, ValidatedImageInfo info, CancellationToken cancellationToken = default)
        {
            var publicId = $"pub_{Guid.NewGuid():N}";
            return Task.FromResult(new StoredImage(
                ProviderPublicId: publicId,
                DeliveryUrl: $"https://res.cloudinary.com/demo/image/upload/{publicId}.png",
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
