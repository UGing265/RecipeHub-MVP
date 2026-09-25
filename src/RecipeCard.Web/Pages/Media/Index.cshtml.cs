using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;
using RecipeCard.Web.Services;

namespace RecipeCard.Web.Pages.Media;

public class IndexModel(
    RecipeDbContext db,
    IImageStorageService imageStorage,
    IImageValidator validator) : PageModel
{
    private readonly RecipeDbContext _db = db;
    private readonly IImageStorageService _imageStorage = imageStorage;
    private readonly IImageValidator _validator = validator;

    public List<MediaItemViewModel> MediaItems { get; set; } = [];

    public string CurrentFilter { get; set; } = "all";

    public int TotalCount { get; set; }
    public int RealCount { get; set; }
    public int AiCount { get; set; }
    public int FailedCount { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public sealed class MediaItemViewModel
    {
        public int Id { get; set; }
        public string DeliveryUrl { get; set; } = string.Empty;
        public string? OriginalFileName { get; set; }
        public long ByteSize { get; set; }
        public string FormattedSize => ByteSize < 1024 * 1024
            ? $"{ByteSize / 1024.0:F1} KB"
            : $"{ByteSize / (1024.0 * 1024.0):F2} MB";
        public MediaSourceType SourceType { get; set; }
        public string SourceLabel => SourceType == MediaSourceType.AiIllustration ? "AI minh họa" : "Ảnh chụp";
        public MediaAssetState State { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int ReferenceCount { get; set; }
        public List<string> ReferencingRecipeNames { get; set; } = [];
    }

    public async Task OnGetAsync(string? filter)
    {
        CurrentFilter = filter?.ToLowerInvariant() switch
        {
            "real" => "real",
            "ai" => "ai",
            "failed" => "failed",
            _ => "all"
        };

        TotalCount = await _db.MediaAssets.CountAsync();
        RealCount = await _db.MediaAssets.CountAsync(m => m.SourceType == MediaSourceType.Real);
        AiCount = await _db.MediaAssets.CountAsync(m => m.SourceType == MediaSourceType.AiIllustration);
        FailedCount = await _db.MediaAssets.CountAsync(m => m.State == MediaAssetState.DeleteFailed);

        var query = _db.MediaAssets
            .Include(m => m.RecipeSteps)
                .ThenInclude(s => s.Recipe)
            .AsNoTracking();

        query = CurrentFilter switch
        {
            "real" => query.Where(m => m.SourceType == MediaSourceType.Real),
            "ai" => query.Where(m => m.SourceType == MediaSourceType.AiIllustration),
            "failed" => query.Where(m => m.State == MediaAssetState.DeleteFailed),
            _ => query
        };

        var assets = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync();

        MediaItems = assets
            .Select(m => new MediaItemViewModel
            {
                Id = m.Id,
                DeliveryUrl = m.DeliveryUrl,
                OriginalFileName = m.OriginalFileName,
                ByteSize = m.ByteSize,
                SourceType = m.SourceType,
                State = m.State,
                CreatedAtUtc = m.CreatedAtUtc,
                ReferenceCount = m.RecipeSteps.Count,
                ReferencingRecipeNames = m.RecipeSteps
                    .Select(s => s.Recipe.Name)
                    .Distinct()
                    .ToList()
            })
            .ToList();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            ErrorMessage = "Vui lòng chọn một file ảnh để tải lên.";
            return RedirectToPage(new { filter = CurrentFilter });
        }

        ValidatedImageInfo info;
        try
        {
            info = _validator.Validate(file);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"File ảnh không hợp lệ: {ex.Message}";
            return RedirectToPage(new { filter = CurrentFilter });
        }

        StoredImage stored;
        try
        {
            using var stream = file.OpenReadStream();
            stored = await _imageStorage.UploadAsync(stream, info);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Tải ảnh lên máy chủ lưu trữ thất bại: {ex.Message}";
            return RedirectToPage(new { filter = CurrentFilter });
        }

        var asset = new MediaAsset
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

        _db.MediaAssets.Add(asset);

        try
        {
            await _db.SaveChangesAsync();
            SuccessMessage = $"Đã tải lên ảnh \"{stored.OriginalFileName ?? "Mới"}\" vào thư viện thành công.";
        }
        catch
        {
            // Compensation
            try
            {
                await _imageStorage.DeleteAsync(stored.ProviderPublicId);
            }
            catch
            {
            }
            throw;
        }

        return RedirectToPage(new { filter = CurrentFilter });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var asset = await _db.MediaAssets
            .Include(m => m.RecipeSteps)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (asset == null)
        {
            return NotFound();
        }

        if (asset.RecipeSteps.Count > 0)
        {
            ErrorMessage = $"Không thể xóa ảnh vì đang được sử dụng trong {asset.RecipeSteps.Count} bước công thức.";
            return RedirectToPage(new { filter = CurrentFilter });
        }

        try
        {
            await _imageStorage.DeleteAsync(asset.ProviderPublicId);
            _db.MediaAssets.Remove(asset);
            await _db.SaveChangesAsync();
            SuccessMessage = "Đã xóa ảnh khỏi thư viện.";
        }
        catch (Exception ex)
        {
            asset.State = MediaAssetState.DeleteFailed;
            await _db.SaveChangesAsync();
            ErrorMessage = $"Không thể xóa ảnh trên máy chủ lưu trữ: {ex.Message}. Trạng thái đã chuyển sang 'Lỗi xóa', có thể thử lại.";
        }

        return RedirectToPage(new { filter = CurrentFilter });
    }

    public async Task<IActionResult> OnPostRetryDeleteAsync(int id)
    {
        var asset = await _db.MediaAssets
            .Include(m => m.RecipeSteps)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (asset == null)
        {
            return NotFound();
        }

        if (asset.RecipeSteps.Count > 0)
        {
            asset.State = MediaAssetState.Active;
            await _db.SaveChangesAsync();
            ErrorMessage = $"Không thể xóa: Ảnh này hiện đang được liên kết trong {asset.RecipeSteps.Count} bước công thức.";
            return RedirectToPage(new { filter = CurrentFilter });
        }

        try
        {
            await _imageStorage.DeleteAsync(asset.ProviderPublicId);
            _db.MediaAssets.Remove(asset);
            await _db.SaveChangesAsync();
            SuccessMessage = "Đã xóa ảnh thành công sau khi thử lại.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Thử lại xóa thất bại: {ex.Message}";
        }

        return RedirectToPage(new { filter = CurrentFilter });
    }
}
