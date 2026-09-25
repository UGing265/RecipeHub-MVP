namespace RecipeCard.Web.Services;

public interface IAiImagePromptBuilder
{
    string BuildVietnameseSourcePrompt(string recipeName, int stepOrder, string instruction, string? userBrief);
    string BuildFinalImagePrompt(string translatedEnglishPrompt);
}

public class AiImagePromptBuilder : IAiImagePromptBuilder
{
    public const int MaxUserBriefLength = 500;
    public const string DefaultStyleGuideline = "Clear beverage preparation photo, commercial food photography, high resolution, realistic lighting, clean neutral studio background, 4k.";

    public string BuildVietnameseSourcePrompt(string recipeName, int stepOrder, string instruction, string? userBrief)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        var promptBuilder = new System.Text.StringBuilder();

        promptBuilder.Append($"Công thức đồ uống: {recipeName.Trim()}. ");
        promptBuilder.Append($"Bước {stepOrder}: {instruction.Trim()}. ");

        if (!string.IsNullOrWhiteSpace(userBrief))
        {
            var cleanBrief = userBrief.Trim();
            if (cleanBrief.Length > MaxUserBriefLength)
            {
                cleanBrief = cleanBrief[..MaxUserBriefLength].Trim();
            }
            promptBuilder.Append($"Ghi chú hình ảnh: {cleanBrief}. ");
        }

        return promptBuilder.ToString().Trim();
    }

    public string BuildFinalImagePrompt(string translatedEnglishPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedEnglishPrompt);

        var cleanEnglish = translatedEnglishPrompt.Trim();
        if (!cleanEnglish.EndsWith('.'))
        {
            cleanEnglish += ".";
        }

        return $"{cleanEnglish} {DefaultStyleGuideline}";
    }
}
