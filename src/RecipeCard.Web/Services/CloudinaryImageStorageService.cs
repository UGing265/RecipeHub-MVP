using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace RecipeCard.Web.Services;

public class CloudinaryImageStorageService : IImageStorageService
{
    private readonly Cloudinary _cloudinary;
    private readonly IImageValidator _validator;

    public CloudinaryImageStorageService(
        IOptions<CloudinaryOptions> options,
        IImageValidator validator)
    {
        ArgumentNullException.ThrowIfNull(options);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));

        var config = options.Value;
        config.Validate();

        var account = new Account(config.CloudName, config.ApiKey, config.ApiSecret);
        _cloudinary = new Cloudinary(account)
        {
            Api = { Secure = true }
        };
    }

    // Constructor for testing or pre-configured Cloudinary instance
    public CloudinaryImageStorageService(Cloudinary cloudinary, IImageValidator validator)
    {
        _cloudinary = cloudinary ?? throw new ArgumentNullException(nameof(cloudinary));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
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
            throw new InvalidOperationException("Không thể kết nối đến máy chủ lưu trữ ảnh Cloudinary.", ex);
        }

        if (uploadResult.Error != null)
        {
            throw new InvalidOperationException($"Lưu trữ ảnh thất bại: {uploadResult.Error.Message}");
        }

        var deliveryUrl = uploadResult.SecureUrl?.ToString() ?? uploadResult.Url?.ToString();
        if (string.IsNullOrEmpty(deliveryUrl))
        {
            throw new InvalidOperationException("Cloudinary không trả về URL phân phối ảnh hợp lệ.");
        }

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
            throw new InvalidOperationException("Không thể kết nối đến máy chủ lưu trữ ảnh để xóa tài nguyên.", ex);
        }

        if (result.Error != null && result.Result != "not found")
        {
            throw new InvalidOperationException($"Lỗi xóa tài nguyên ảnh trên Cloudinary: {result.Error.Message}");
        }
    }
}
