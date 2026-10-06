using System.Diagnostics;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class CloudinaryImageStorageService : IImageStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly IImageValidator _validator;
    private readonly ILogger<CloudinaryImageStorageService>? _logger;

    public CloudinaryImageStorageService(
        IOptions<CloudinaryOptions> options,
        IImageValidator validator,
        ILogger<CloudinaryImageStorageService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger;

        var config = options.Value;
        config.Validate();

        var account = new Account(config.CloudName, config.ApiKey, config.ApiSecret);
        _cloudinary = new Cloudinary(account)
        {
            Api = { Secure = true }
        };
    }

    // Constructor for testing or pre-configured Cloudinary instance
    public CloudinaryImageStorageService(
        Cloudinary cloudinary,
        IImageValidator validator,
        ILogger<CloudinaryImageStorageService>? logger = null)
    {
        _cloudinary = cloudinary ?? throw new ArgumentNullException(nameof(cloudinary));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _logger = logger;
    }

    public async Task<StoredImage> UploadAsync(
        Stream image,
        ValidatedImageInfo info,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);

        if (image.CanSeek)
        {
            image.Position = 0;
        }

        var fileName = !string.IsNullOrWhiteSpace(info.OriginalFileName)
            ? Path.GetFileName(info.OriginalFileName)
            : $"image{info.Extension}";

        _logger?.LogInformation("[CLOUDINARY] Đang tải ảnh lên Cloudinary...");
        var stopwatch = Stopwatch.StartNew();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, image),
            Folder = "recipe-hub/media",
            UniqueFilename = true,
            Overwrite = false
        };

        ImageUploadResult uploadResult;
        try
        {
            uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger?.LogError("[CLOUDINARY] -> Không thể kết nối Cloudinary: {Message}", ex.Message);
            throw new InvalidOperationException("Không thể kết nối đến máy chủ lưu trữ ảnh Cloudinary.", ex);
        }

        if (uploadResult.Error != null)
        {
            _logger?.LogError("[CLOUDINARY] -> Tải lên THẤT BẠI: {Error}", uploadResult.Error.Message);
            throw new InvalidOperationException($"Lưu trữ ảnh thất bại: {uploadResult.Error.Message}");
        }

        var deliveryUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString();
        if (string.IsNullOrEmpty(deliveryUrl))
        {
            _logger?.LogError("[CLOUDINARY] -> Thất bại: Không nhận được URL ảnh từ Cloudinary.");
            throw new InvalidOperationException("Cloudinary không trả về URL phân phối ảnh hợp lệ.");
        }

        _logger?.LogInformation("[CLOUDINARY] -> Tải lên THÀNH CÔNG ({ElapsedMs}ms) -> URL: {Url}",
            stopwatch.ElapsedMilliseconds, deliveryUrl);

        return new StoredImage(
            ProviderPublicId: uploadResult.PublicId,
            DeliveryUrl: deliveryUrl,
            ByteSize: uploadResult.Bytes > 0 ? uploadResult.Bytes : info.ByteSize,
            MimeType: info.MimeType,
            OriginalFileName: info.OriginalFileName
        );
    }

    public async Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPublicId))
        {
            throw new ArgumentException("Mã định danh ảnh (PublicId) không được để trống.", nameof(providerPublicId));
        }

        _logger?.LogInformation("[Cloudinary API] Đang gửi yêu cầu xóa ảnh PublicId: {PublicId}", providerPublicId);

        var deleteParams = new DeletionParams(providerPublicId)
        {
            ResourceType = ResourceType.Image
        };

        DeletionResult result;
        try
        {
            result = await _cloudinary.DestroyAsync(deleteParams);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger?.LogError(ex, "[Cloudinary API] Lỗi kết nối khi xóa PublicId {PublicId}: {Message}", providerPublicId, ex.Message);
            throw new InvalidOperationException("Không thể kết nối đến máy chủ lưu trữ ảnh để xóa tài nguyên.", ex);
        }

        if (result.Error != null && result.Result != "not found")
        {
            _logger?.LogError("[Cloudinary API] Lỗi xóa ảnh PublicId {PublicId}: {Error}", providerPublicId, result.Error.Message);
            throw new InvalidOperationException($"Lỗi xóa tài nguyên ảnh trên Cloudinary: {result.Error.Message}");
        }

        _logger?.LogInformation("[Cloudinary API] Xóa ảnh thành công trên Cloudinary (PublicId: {PublicId}, Result: {Result})",
            providerPublicId, result.Result);
    }
}
