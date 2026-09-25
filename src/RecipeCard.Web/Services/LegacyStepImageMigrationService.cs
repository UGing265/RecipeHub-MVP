using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public record MigrationReport(
    int TotalScanned,
    int Converted,
    int Missing,
    int Invalid,
    int ProviderFailed,
    int DatabaseFailed,
    List<string> Details
);

public class LegacyStepImageMigrationService(
    RecipeDbContext db,
    IImageStorageService imageStorage,
    IImageValidator validator,
    IWebHostEnvironment environment)
{
    private readonly RecipeDbContext _db = db;
    private readonly IImageStorageService _imageStorage = imageStorage;
    private readonly IImageValidator _validator = validator;
    private readonly string _legacyFolder = Path.Combine(
        string.IsNullOrEmpty(environment.WebRootPath) ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot") : environment.WebRootPath,
        "uploads",
        "steps"
    );

    public async Task<MigrationReport> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var stepsToMigrate = await _db.RecipeSteps
            .Where(s => s.ImageFileName != null && s.MediaAssetId == null)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        int total = stepsToMigrate.Count;
        int converted = 0;
        int missing = 0;
        int invalid = 0;
        int providerFailed = 0;
        int databaseFailed = 0;
        var details = new List<string>();

        foreach (var step in stepsToMigrate)
        {
            var fileName = step.ImageFileName!;
            var cleanFileName = Path.GetFileName(fileName);
            if (cleanFileName != fileName)
            {
                invalid++;
                details.Add($"Step #{step.Id}: Tên file không hợp lệ (path traversal): {fileName}");
                continue;
            }

            var fullPath = Path.Combine(_legacyFolder, cleanFileName);
            if (!File.Exists(fullPath))
            {
                missing++;
                details.Add($"Step #{step.Id}: File không tồn tại trên đĩa: {cleanFileName}");
                continue;
            }

            ValidatedImageInfo info;
            try
            {
                var fileBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
                info = _validator.ValidateBytes(fileBytes, cleanFileName);
            }
            catch (Exception ex)
            {
                invalid++;
                details.Add($"Step #{step.Id}: Kiểm tra tính hợp lệ file thất bại ({cleanFileName}): {ex.Message}");
                continue;
            }

            StoredImage stored;
            try
            {
                await using var stream = File.OpenRead(fullPath);
                stored = await _imageStorage.UploadAsync(stream, info, cancellationToken);
            }
            catch (Exception ex)
            {
                providerFailed++;
                details.Add($"Step #{step.Id}: Tải lên lưu trữ thất bại ({cleanFileName}): {ex.Message}");
                continue;
            }

            try
            {
                var mediaAsset = new MediaAsset
                {
                    StorageProvider = "Cloudinary",
                    ProviderPublicId = stored.ProviderPublicId,
                    DeliveryUrl = stored.DeliveryUrl,
                    OriginalFileName = cleanFileName,
                    MimeType = stored.MimeType,
                    ByteSize = stored.ByteSize,
                    SourceType = MediaSourceType.Real,
                    State = MediaAssetState.Active,
                    CreatedAtUtc = DateTime.UtcNow
                };

                _db.MediaAssets.Add(mediaAsset);
                step.MediaAsset = mediaAsset;
                await _db.SaveChangesAsync(cancellationToken);

                converted++;
                details.Add($"Step #{step.Id}: Chuyển đổi thành công sang MediaAsset #{mediaAsset.Id} ({stored.DeliveryUrl})");
            }
            catch (Exception ex)
            {
                databaseFailed++;
                details.Add($"Step #{step.Id}: Lưu vào CSDL thất bại ({cleanFileName}): {ex.Message}");

                // Compensation: best-effort delete from remote storage
                try
                {
                    await _imageStorage.DeleteAsync(stored.ProviderPublicId, cancellationToken);
                }
                catch
                {
                    // Ignore compensation failure
                }
            }
        }

        return new MigrationReport(
            TotalScanned: total,
            Converted: converted,
            Missing: missing,
            Invalid: invalid,
            ProviderFailed: providerFailed,
            DatabaseFailed: databaseFailed,
            Details: details
        );
    }
}
