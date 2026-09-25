namespace RecipeCard.Web.Services;

public interface IAiPromptTranslator
{
    Task<string> TranslateVietnameseToEnglishAsync(
        string vietnamesePrompt,
        CancellationToken cancellationToken = default);
}
