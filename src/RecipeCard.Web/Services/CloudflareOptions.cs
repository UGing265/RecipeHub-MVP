namespace RecipeCard.Web.Services;

public class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    public string AccountId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string Model { get; set; } = "@cf/black-forest-labs/flux-1-schnell";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountId))
        {
            throw new InvalidOperationException("Cấu hình Cloudflare chưa hợp lệ: AccountId không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(ApiToken))
        {
            throw new InvalidOperationException("Cấu hình Cloudflare chưa hợp lệ: ApiToken không được để trống.");
        }
    }
}
