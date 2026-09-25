namespace RecipeCard.Web.Services;

public interface IAiImagePromptBuilder
{
    string BuildPrompt(string recipeName, int stepOrder, string instruction, string? userBrief);
}

public class AiImagePromptBuilder : IAiImagePromptBuilder
{
    public const int MaxUserBriefLength = 500;
    public const string DefaultStyleGuideline = "Clear beverage preparation photo, commercial food photography, high resolution, realistic lighting, clean neutral studio background, 4k.";

    public string BuildPrompt(string recipeName, int stepOrder, string instruction, string? userBrief)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);

        var promptBuilder = new System.Text.StringBuilder();

        // 1. Context: Recipe & Step
        promptBuilder.Append($"Beverage recipe: {recipeName.Trim()}. ");
        promptBuilder.Append($"Step {stepOrder}: {instruction.Trim()}. ");

        // 2. User brief (if provided, sanitized and bounded)
        if (!string.IsNullOrWhiteSpace(userBrief))
        {
            var cleanBrief = userBrief.Trim();
            if (cleanBrief.Length > MaxUserBriefLength)
            {
                cleanBrief = cleanBrief[..MaxUserBriefLength].Trim();
            }
            promptBuilder.Append($"Additional visual direction: {cleanBrief}. ");
        }

        // 3. Style guidelines
        promptBuilder.Append(DefaultStyleGuideline);

        return promptBuilder.ToString();
    }
}
