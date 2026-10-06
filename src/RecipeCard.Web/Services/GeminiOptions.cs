namespace RecipeCard.Web.Services;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public List<string> ApiKeys { get; set; } = [];

    public void Validate()
    {
        if (ApiKeys == null || ApiKeys.Count == 0)
        {
            throw new InvalidOperationException("Cấu hình Gemini chưa hợp lệ: Cần cấu hình từ 1 đến 3 API key trong Gemini:ApiKeys.");
        }

        if (ApiKeys.Count > 3)
        {
            throw new InvalidOperationException("Cấu hình Gemini chưa hợp lệ: Số lượng API key vượt quá giới hạn tối đa 3 key.");
        }

        for (var i = 0; i < ApiKeys.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(ApiKeys[i]))
            {
                throw new InvalidOperationException($"Cấu hình Gemini chưa hợp lệ: API key tại vị trí {i} không được để trống hoặc chỉ chứa khoảng trắng.");
            }
        }

        var distinctCount = ApiKeys.Distinct(StringComparer.Ordinal).Count();
        if (distinctCount != ApiKeys.Count)
        {
            throw new InvalidOperationException("Cấu hình Gemini chưa hợp lệ: Danh sách API key chứa các khóa trùng lặp.");
        }
    }
}
