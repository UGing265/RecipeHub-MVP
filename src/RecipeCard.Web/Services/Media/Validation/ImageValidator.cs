using Microsoft.AspNetCore.Http;

namespace RecipeCard.Web.Services;

public record ValidatedImageInfo(
    string Extension,
    string MimeType,
    long ByteSize,
    string? OriginalFileName = null
);

public interface IImageValidator
{
    ValidatedImageInfo Validate(IFormFile file);
    ValidatedImageInfo Validate(Stream stream, string fileName, long length);
    ValidatedImageInfo ValidateBytes(byte[] bytes, string? suggestedFileName = null);
}

public class ImageValidator : IImageValidator
{
    public const long MaxFileSize = 5 * 1024 * 1024; // 5 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public ValidatedImageInfo Validate(IFormFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length <= 0)
        {
            throw new InvalidOperationException("File ảnh không có nội dung.");
        }

        if (file.Length > MaxFileSize)
        {
            throw new InvalidOperationException("Dung lượng file ảnh không được vượt quá 5MB.");
        }

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            throw new InvalidOperationException("Định dạng file không hợp lệ. Chỉ chấp nhận .jpg, .jpeg, .png, .webp.");
        }

        using var stream = file.OpenReadStream();
        var mimeType = DetermineMimeTypeAndVerifyHeader(stream, ext);

        return new ValidatedImageInfo(
            Extension: ext.ToLowerInvariant(),
            MimeType: mimeType,
            ByteSize: file.Length,
            OriginalFileName: Path.GetFileName(file.FileName)
        );
    }

    public ValidatedImageInfo Validate(Stream stream, string fileName, long length)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (length <= 0)
        {
            throw new InvalidOperationException("Nội dung ảnh không được rỗng.");
        }

        if (length > MaxFileSize)
        {
            throw new InvalidOperationException("Dung lượng ảnh không được vượt quá 5MB.");
        }

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            throw new InvalidOperationException("Định dạng file không hợp lệ. Chỉ chấp nhận .jpg, .jpeg, .png, .webp.");
        }

        var mimeType = DetermineMimeTypeAndVerifyHeader(stream, ext);

        return new ValidatedImageInfo(
            Extension: ext.ToLowerInvariant(),
            MimeType: mimeType,
            ByteSize: length,
            OriginalFileName: Path.GetFileName(fileName)
        );
    }

    public ValidatedImageInfo ValidateBytes(byte[] bytes, string? suggestedFileName = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length <= 0)
        {
            throw new InvalidOperationException("Dữ liệu ảnh không có nội dung.");
        }

        if (bytes.Length > MaxFileSize)
        {
            throw new InvalidOperationException("Dung lượng ảnh không được vượt quá 5MB.");
        }

        using var ms = new MemoryStream(bytes);
        string ext;
        string mimeType;

        if (!string.IsNullOrEmpty(suggestedFileName))
        {
            var suggestedExt = Path.GetExtension(suggestedFileName);
            if (!string.IsNullOrEmpty(suggestedExt))
            {
                if (!AllowedExtensions.Contains(suggestedExt))
                {
                    throw new InvalidOperationException("Định dạng file không hợp lệ. Chỉ chấp nhận .jpg, .jpeg, .png, .webp.");
                }
                ext = suggestedExt.ToLowerInvariant();
                mimeType = DetermineMimeTypeAndVerifyHeader(ms, ext);
                return new ValidatedImageInfo(
                    Extension: ext,
                    MimeType: mimeType,
                    ByteSize: bytes.Length,
                    OriginalFileName: Path.GetFileName(suggestedFileName)
                );
            }
        }

        // Detect from header
        (ext, mimeType) = DetectExtensionAndMime(ms);

        return new ValidatedImageInfo(
            Extension: ext,
            MimeType: mimeType,
            ByteSize: bytes.Length,
            OriginalFileName: suggestedFileName != null ? Path.GetFileName(suggestedFileName) : $"generated{ext}"
        );
    }

    private static string DetermineMimeTypeAndVerifyHeader(Stream stream, string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (!IsValidImageHeader(stream, ext))
        {
            throw new InvalidOperationException("Nội dung file không khớp với định dạng ảnh hợp lệ.");
        }

        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => throw new InvalidOperationException("Định dạng ảnh không được hỗ trợ.")
        };
    }

    private static (string Extension, string MimeType) DetectExtensionAndMime(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var originalPos = stream.CanSeek ? stream.Position : 0;
        var bytesRead = stream.Read(header);
        if (stream.CanSeek)
        {
            stream.Position = originalPos;
        }

        if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }

        if (bytesRead >= 4 && header[0] == 0x89 && header[1] == 0x40 + 0x10 && header[2] == 0x4E && header[3] == 0x47)
        {
            return (".png", "image/png");
        }

        if (bytesRead >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return (".webp", "image/webp");
        }

        throw new InvalidOperationException("Nội dung không phải là định dạng ảnh được hỗ trợ (.jpg, .png, .webp).");
    }

    public static bool IsValidImageHeader(Stream stream, string extension)
    {
        Span<byte> header = stackalloc byte[12];
        var originalPos = stream.CanSeek ? stream.Position : 0;
        var bytesRead = stream.Read(header);
        if (stream.CanSeek)
        {
            stream.Position = originalPos;
        }

        if (bytesRead < 4)
        {
            return false;
        }

        var ext = extension.ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg")
        {
            return header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }

        if (ext is ".png")
        {
            return header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        }

        if (ext is ".webp")
        {
            return bytesRead >= 12 &&
                   header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                   header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50;
        }

        return false;
    }
}
