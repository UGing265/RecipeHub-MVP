using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pages.Recipes;
using RecipeCard.Web.Services;
using Xunit;
using MediaIndexModel = RecipeCard.Web.Pages.Media.IndexModel;

namespace RecipeCard.Web.Tests;

public class MediaLibraryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RecipeDbContext _db;
    private readonly FakeStorageService _storage;
    private readonly ImageValidator _validator;

    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public MediaLibraryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<RecipeDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new RecipeDbContext(options);
        _db.Database.EnsureCreated();

        _storage = new FakeStorageService();
        _validator = new ImageValidator();
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task Upload_creates_active_media_asset()
    {
        var model = new MediaIndexModel(_db, _storage, _validator);
        var formFile = new FormFile(new MemoryStream(ValidPngBytes), 0, ValidPngBytes.Length, "file", "photo.png");
        var result = await model.OnPostUploadAsync(formFile);
        Assert.IsType<RedirectToPageResult>(result);

        var asset = await _db.MediaAssets.FirstOrDefaultAsync();
        Assert.NotNull(asset);
        Assert.Equal("photo.png", asset.OriginalFileName);
        Assert.Equal(MediaSourceType.Real, asset.SourceType);
        Assert.Equal(MediaAssetState.Active, asset.State);
        Assert.StartsWith("https://res.cloudinary.com", asset.DeliveryUrl);
    }

    [Fact]
    public async Task Delete_fails_when_asset_is_referenced_by_step()
    {
        var asset = new MediaAsset
        {
            ProviderPublicId = "asset_in_use",
            DeliveryUrl = "https://res.cloudinary.com/demo/image/upload/asset_in_use.jpg",
            SourceType = MediaSourceType.Real,
            State = MediaAssetState.Active
        };
        _db.MediaAssets.Add(asset);

        var recipe = new Recipe { Name = "Recipe with asset" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var step = new RecipeStep
        {
            RecipeId = recipe.Id,
            SortOrder = 1,
            Instruction = "Step using asset",
            MediaAssetId = asset.Id
        };
        _db.RecipeSteps.Add(step);
        await _db.SaveChangesAsync();

        var model = new MediaIndexModel(_db, _storage, _validator);
        var result = await model.OnPostDeleteAsync(asset.Id);
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("Không thể xóa ảnh vì đang được sử dụng", model.ErrorMessage);

        // Asset must still exist
        var existingAsset = await _db.MediaAssets.FindAsync(asset.Id);
        Assert.NotNull(existingAsset);
    }

    [Fact]
    public async Task Delete_succeeds_for_unreferenced_asset()
    {
        var asset = new MediaAsset
        {
            ProviderPublicId = "asset_unused",
            DeliveryUrl = "https://res.cloudinary.com/demo/image/upload/asset_unused.jpg",
            SourceType = MediaSourceType.Real,
            State = MediaAssetState.Active
        };
        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync();

        var model = new MediaIndexModel(_db, _storage, _validator);
        var result = await model.OnPostDeleteAsync(asset.Id);
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Null(model.ErrorMessage);

        var existingAsset = await _db.MediaAssets.FindAsync(asset.Id);
        Assert.Null(existingAsset);
        Assert.Contains(asset.ProviderPublicId, _storage.DeletedPublicIds);
    }

    [Fact]
    public async Task Delete_marks_state_DeleteFailed_when_provider_fails()
    {
        _storage.ThrowOnDelete = true;

        var asset = new MediaAsset
        {
            ProviderPublicId = "failing_asset",
            DeliveryUrl = "https://res.cloudinary.com/demo/image/upload/failing_asset.jpg",
            SourceType = MediaSourceType.Real,
            State = MediaAssetState.Active
        };
        _db.MediaAssets.Add(asset);
        await _db.SaveChangesAsync();

        var model = new MediaIndexModel(_db, _storage, _validator);
        await model.OnPostDeleteAsync(asset.Id);

        Assert.Contains("Lỗi xóa", model.ErrorMessage);

        var existingAsset = await _db.MediaAssets.FindAsync(asset.Id);
        Assert.NotNull(existingAsset);
        Assert.Equal(MediaAssetState.DeleteFailed, existingAsset.State);
    }

    [Fact]
    public async Task AddStep_reuses_existing_media_asset_without_upload()
    {
        var asset = new MediaAsset
        {
            ProviderPublicId = "shared_asset",
            DeliveryUrl = "https://res.cloudinary.com/demo/image/upload/shared_asset.jpg",
            SourceType = MediaSourceType.Real,
            State = MediaAssetState.Active
        };
        _db.MediaAssets.Add(asset);

        var recipe = new Recipe { Name = "Recipe Sharing Media" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var editModel = new EditModel(_db, _storage, _validator);

        // Add Step 1 using the asset
        await editModel.OnPostAddStepAsync(recipe.Id, "Step 1", image: null, mediaAssetId: asset.Id);

        // Add Step 2 using the same asset
        await editModel.OnPostAddStepAsync(recipe.Id, "Step 2", image: null, mediaAssetId: asset.Id);

        var steps = await _db.RecipeSteps.Where(s => s.RecipeId == recipe.Id).OrderBy(s => s.SortOrder).ToListAsync();
        Assert.Equal(2, steps.Count);
        Assert.Equal(asset.Id, steps[0].MediaAssetId);
        Assert.Equal(asset.Id, steps[1].MediaAssetId);

        // Upload was never called since we reused existing asset
        Assert.Empty(_storage.UploadedPublicIds);
    }

    [Fact]
    public async Task AddStep_rejects_both_image_and_media_asset_selection()
    {
        var recipe = new Recipe { Name = "Recipe Mutex" };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        var editModel = new EditModel(_db, _storage, _validator);
        var formFile = new FormFile(new MemoryStream(ValidPngBytes), 0, ValidPngBytes.Length, "file", "photo.png");

        await editModel.OnPostAddStepAsync(recipe.Id, "Step Instruction", image: formFile, mediaAssetId: 99);

        Assert.Contains("Chỉ được chọn 1 nguồn ảnh", editModel.ErrorMessage);
    }

    private sealed class FakeStorageService : IImageStorageService
    {
        public bool ThrowOnDelete { get; set; }
        public List<string> UploadedPublicIds { get; } = [];
        public List<string> DeletedPublicIds { get; } = [];

        public Task<StoredImage> UploadAsync(Stream image, ValidatedImageInfo info, CancellationToken cancellationToken = default)
        {
            var publicId = $"pub_{Guid.NewGuid():N}";
            UploadedPublicIds.Add(publicId);
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
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("Simulated provider deletion failure.");
            }

            DeletedPublicIds.Add(providerPublicId);
            return Task.CompletedTask;
        }
    }
}
