namespace RecipeCard.Web.Services;

public class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CloudName))
        {
            throw new InvalidOperationException("Cấu hình Cloudinary chưa hợp lệ: CloudName không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Cấu hình Cloudinary chưa hợp lệ: ApiKey không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(ApiSecret))
        {
            throw new InvalidOperationException("Cấu hình Cloudinary chưa hợp lệ: ApiSecret không được để trống.");
        }
    }
}
