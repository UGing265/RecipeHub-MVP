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
        "Clear beverage preparation process photo, commercial beverage photography, high resolution, realistic studio lighting, clean neutral bar background, 4k. " +
        "Depict only the exact action described in the target instruction. " +
        "Only bar professional hands or forearms visible performing the action; no full person, no face, no head, no body, no provocative posture, no sensual framing. " +
        "Strictly no extra equipment, no extra machinery, no extra ingredients, and no future preparation steps beyond the target. " +
        "No text, no letters, no watermark, no labels, no numbers, no words.";

    public const string FinalProductCanonicalStyle =
        "Finished plated beverage hero photography, commercial food and beverage showcase, high resolution, realistic studio lighting, clean neutral background, 4k. " +
        "Depict only the finished served drink as a complete product ready to enjoy. " +
        "No human hands, no people, no preparation process, no preparation steps, no pouring action, no shaker action, no blender action, no cluttered props. " +
        "No text, no letters, no watermark, no labels, no numbers, no words.";

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

        sb.AppendLine("TARGET: STEP INSTRUCTION");
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

        var finalPrompt = $"{cleanEnglish} {style}".Trim();

        if (finalPrompt.Length > MaxFinalPromptLength)
        {
            throw new InvalidOperationException(
                $"Prompt hoàn chỉnh vượt quá giới hạn cho phép ({finalPrompt.Length}/{MaxFinalPromptLength} ký tự). Vui lòng rút gọn ghi chú hoặc các bước công thức.");
        }

        return finalPrompt;
    }
}
