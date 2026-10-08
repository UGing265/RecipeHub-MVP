using System.Text;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public interface IAiImagePromptBuilder
{
    string BuildVietnameseSourcePrompt(string recipeName, int stepOrder, string instruction, string? userBrief);

    string BuildStepSourcePrompt(
        string recipeName,
        IEnumerable<(string Name, decimal Quantity, string Unit)> ingredients,
        IEnumerable<(int SortOrder, string Instruction)> priorSteps,
        int currentSortOrder,
        string currentInstruction,
        string? userBrief);

    string BuildFinalProductSourcePrompt(
        string recipeName,
        IEnumerable<(string Name, decimal Quantity, string Unit)> ingredients,
        IEnumerable<(int SortOrder, string Instruction)> allSteps,
        string? generalNote,
        string? userBrief);

    string BuildFinalImagePrompt(string translatedEnglishPrompt);

    string AttachCanonicalStyle(AiDraftTargetKind targetKind, string translatedEnglishPrompt);
}

public class AiImagePromptBuilder : IAiImagePromptBuilder
{
    public const int MaxUserBriefLength = 500;
    public const int MaxFinalPromptLength = AiPromptTranslationConstants.MaxFinalPromptLength;
    public const string StepCanonicalStyle =
        "Commercial beverage preparation photography, extreme close-up first-person POV shot angled down at the bar counter, 4k. " +
        "Top-down and 45-degree close-up focus directly on the hands and tools on the work surface. " +
        "Workstation tabletop perspective: hands and the vessel are the sole visible subjects, empty unoccupied background. " +
        "Strictly pure photograph only: NO text, NO typography, NO words, NO letters, NO numbers, NO labels, NO logo, NO watermark.";

    public const string FinalProductCanonicalStyle =
        "High-end commercial beverage product photography, shot at a 45-degree angle showing both the drink surface and the glass profile, centered hero shot, the single beverage glass is prominently featured, fully visible with comfortable space above and below, 4k. " +
        "Exquisite drink presentation, served in a wide short tumbler glass with generous width, pure vibrant beverage colors, clear sparkling ice cubes, condensation water droplets on glass, soft natural lighting and shadow. " +
        "Clean aesthetic composition, tasteful background styling harmonizing with the beverage. " +
        "Strictly pure photograph only: NO text, NO typography, NO words, NO letters, NO numbers, NO labels, NO logo, NO watermark.";

    public const string DefaultStyleGuideline = StepCanonicalStyle;

    public string BuildVietnameseSourcePrompt(string recipeName, int stepOrder, string instruction, string? userBrief)
    {
        return BuildStepSourcePrompt(
            recipeName,
            [],
            [],
            stepOrder,
            instruction,
            userBrief);
    }

    public string BuildStepSourcePrompt(
        string recipeName,
        IEnumerable<(string Name, decimal Quantity, string Unit)> ingredients,
        IEnumerable<(int SortOrder, string Instruction)> priorSteps,
        int currentSortOrder,
        string currentInstruction,
        string? userBrief)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentInstruction);

        var sb = new StringBuilder();

        sb.AppendLine("TARGET: STEP ACTION");
        sb.AppendLine($"RECIPE: {recipeName.Trim()}");

        var ingredientList = ingredients?.ToList() ?? [];
        if (ingredientList.Count > 0)
        {
            sb.AppendLine("INGREDIENTS:");
            foreach (var ing in ingredientList)
            {
                var qtyStr = ing.Quantity.ToString("G29");
                sb.AppendLine($"- {ing.Name.Trim()}: {qtyStr} {ing.Unit.Trim()}");
            }
        }

        var priorList = priorSteps?.OrderBy(s => s.SortOrder).ToList() ?? [];
        if (priorList.Count > 0)
        {
            sb.AppendLine("PRIOR STEPS (CONTEXT ONLY):");
            foreach (var p in priorList)
            {
                sb.AppendLine($"- Bước {p.SortOrder}: {p.Instruction.Trim()}");
            }
        }

        sb.AppendLine($"CURRENT STEP: Bước {currentSortOrder}: {currentInstruction.Trim()}");

        if (!string.IsNullOrWhiteSpace(userBrief))
        {
            var cleanBrief = userBrief.Trim();
            if (cleanBrief.Length > MaxUserBriefLength)
            {
                cleanBrief = cleanBrief[..MaxUserBriefLength].Trim();
            }
            sb.AppendLine($"USER VISUAL NOTE: {cleanBrief}");
        }

        return sb.ToString().Trim();
    }

    public string BuildFinalProductSourcePrompt(
        string recipeName,
        IEnumerable<(string Name, decimal Quantity, string Unit)> ingredients,
        IEnumerable<(int SortOrder, string Instruction)> allSteps,
        string? generalNote,
        string? userBrief)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);

        var sb = new StringBuilder();

        sb.AppendLine("TARGET: FINAL PRODUCT HERO");
        sb.AppendLine($"RECIPE: {recipeName.Trim()}");

        var ingredientList = ingredients?.ToList() ?? [];
        if (ingredientList.Count > 0)
        {
            sb.AppendLine("INGREDIENTS:");
            foreach (var ing in ingredientList)
            {
                var qtyStr = ing.Quantity.ToString("G29");
                sb.AppendLine($"- {ing.Name.Trim()}: {qtyStr} {ing.Unit.Trim()}");
            }
        }

        var stepList = allSteps?.OrderBy(s => s.SortOrder).ToList() ?? [];
        if (stepList.Count > 0)
        {
            sb.AppendLine("ALL PROCESS STEPS (CONTEXT ONLY):");
            foreach (var s in stepList)
            {
                sb.AppendLine($"- Bước {s.SortOrder}: {s.Instruction.Trim()}");
            }
        }

        if (!string.IsNullOrWhiteSpace(generalNote))
        {
            sb.AppendLine($"GENERAL RECIPE NOTE: {generalNote.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(userBrief))
        {
            var cleanBrief = userBrief.Trim();
            if (cleanBrief.Length > MaxUserBriefLength)
            {
                cleanBrief = cleanBrief[..MaxUserBriefLength].Trim();
            }
            sb.AppendLine($"USER VISUAL NOTE: {cleanBrief}");
        }

        return sb.ToString().Trim();
    }

    public string BuildFinalImagePrompt(string translatedEnglishPrompt)
    {
        return AttachCanonicalStyle(AiDraftTargetKind.StepInstruction, translatedEnglishPrompt);
    }

    public string AttachCanonicalStyle(AiDraftTargetKind targetKind, string translatedEnglishPrompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedEnglishPrompt);

        var cleanEnglish = translatedEnglishPrompt.Trim();
        if (!cleanEnglish.EndsWith('.'))
        {
            cleanEnglish += ".";
        }

        var style = targetKind switch
        {
            AiDraftTargetKind.FinalProduct => FinalProductCanonicalStyle,
            _ => StepCanonicalStyle
        };

        // Placing camera/photographic directives first prevents FLUX from misinterpreting text as a poster
        var finalPrompt = $"{style} The beverage and recipe details: {cleanEnglish} Remember: pure photograph only, zero text on image.".Trim();

        if (finalPrompt.Length > MaxFinalPromptLength)
        {
            throw new InvalidOperationException(
                $"Prompt hoàn chỉnh vượt quá giới hạn cho phép ({finalPrompt.Length}/{MaxFinalPromptLength} ký tự). Vui lòng rút gọn ghi chú hoặc các bước công thức.");
        }

        return finalPrompt;
    }
}
