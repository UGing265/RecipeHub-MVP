using Microsoft.AspNetCore.Hosting;

namespace RecipeCard.Web.Services;

public interface IAiDraftFileStore
{
    Task<string> SaveDraftAsync(Guid draftId, byte[] imageBytes, string extension, CancellationToken cancellationToken = default);
    Task<byte[]> ReadDraftAsync(string fileName, CancellationToken cancellationToken = default);
    bool DeleteDraft(string? fileName);
    string GetSafePath(string fileName);
}

public class AiDraftFileStore : IAiDraftFileStore
{
    private readonly string _draftFolder;

    public AiDraftFileStore(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var contentRoot = environment.ContentRootPath;
        if (string.IsNullOrEmpty(contentRoot))
        {
            contentRoot = Directory.GetCurrentDirectory();
        }

        _draftFolder = Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "ai-drafts"));
    }

    public async Task<string> SaveDraftAsync(Guid draftId, byte[] imageBytes, string extension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("Dữ liệu ảnh nháp không được rỗng.", nameof(imageBytes));
        }

        Directory.CreateDirectory(_draftFolder);

        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";
        var fileName = $"{draftId:N}{ext}";
        var fullPath = GetSafePath(fileName);

        await File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);
        return fileName;
    }

    public async Task<byte[]> ReadDraftAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafePath(fileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Không tìm thấy file ảnh nháp tạm thời.", fileName);
        }

        return await File.ReadAllBytesAsync(fullPath, cancellationToken);
    }

    public bool DeleteDraft(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        try
        {
            var fullPath = GetSafePath(fileName);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }
        }
        catch
        {
            // Suppress to avoid crashing background operations
        }

        return false;
    }

    public string GetSafePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Tên file không được để trống.", nameof(fileName));
        }

        var cleanName = Path.GetFileName(fileName);
        if (cleanName != fileName)
        {
            throw new InvalidOperationException("Phát hiện đường dẫn không an toàn.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_draftFolder, cleanName));
        if (!fullPath.StartsWith(_draftFolder, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn file nằm ngoài thư mục lưu trữ ảnh nháp.");
        }

        return fullPath;
    }
}
