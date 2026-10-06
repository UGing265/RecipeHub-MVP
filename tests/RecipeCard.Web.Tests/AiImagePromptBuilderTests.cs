using RecipeCard.Web.Models;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class AiImagePromptBuilderTests
{
    private readonly AiImagePromptBuilder _builder = new();

    [Fact]
    public void BuildStepSourcePrompt_includes_ingredients_and_prior_steps_in_order_and_excludes_future_steps()
    {
        var ingredients = new List<(string Name, decimal Quantity, string Unit)>
        {
            ("Trà đen", 10m, "g"),
            ("Sữa đặc", 30m, "ml")
        };

        var priorSteps = new List<(int SortOrder, string Instruction)>
        {
            (1, "Ủ trà đen với 150ml nước sôi trong 10 phút.")
        };

        var prompt = _builder.BuildStepSourcePrompt(
            recipeName: "Trà sữa truyền thống",
            ingredients: ingredients,
            priorSteps: priorSteps,
            currentSortOrder: 2,
            currentInstruction: "Đong 30ml sữa đặc vào ly.",
            userBrief: "Ly thủy tinh cao");

        Assert.Contains("TARGET: STEP INSTRUCTION", prompt);
        Assert.Contains("RECIPE: Trà sữa truyền thống", prompt);
        Assert.Contains("INGREDIENTS:", prompt);
        Assert.Contains("- Trà đen: 10 g", prompt);
        Assert.Contains("- Sữa đặc: 30 ml", prompt);
        Assert.Contains("PRIOR STEPS (CONTEXT ONLY):", prompt);
        Assert.Contains("- Bước 1: Ủ trà đen với 150ml nước sôi trong 10 phút.", prompt);
        Assert.Contains("CURRENT STEP: Bước 2: Đong 30ml sữa đặc vào ly.", prompt);
        Assert.Contains("USER VISUAL NOTE: Ly thủy tinh cao", prompt);

        // Does not contain any future steps
        Assert.DoesNotContain("Bước 3", prompt);
    }

    [Fact]
    public void BuildFinalProductSourcePrompt_includes_all_steps_and_general_note_without_current_step_label()
    {
        var ingredients = new List<(string Name, decimal Quantity, string Unit)>
        {
            ("Cà phê hạt robusta", 20m, "g"),
            ("Sữa tươi không đường", 100m, "ml")
        };

        var allSteps = new List<(int SortOrder, string Instruction)>
        {
            (1, "Chiết xuất 40ml espresso."),
            (2, "Đánh nóng sữa tươi tạo bọt mịn."),
            (3, "Rót sữa vào espresso tạo hình latte art.")
        };

        var prompt = _builder.BuildFinalProductSourcePrompt(
            recipeName: "Cà phê Latte",
            ingredients: ingredients,
            allSteps: allSteps,
            generalNote: "Phục vụ nóng trong tách gốm sứ trắng",
            userBrief: "Tách đặt trên đĩa lót");

        Assert.Contains("TARGET: FINAL PRODUCT HERO", prompt);
        Assert.Contains("RECIPE: Cà phê Latte", prompt);
        Assert.Contains("ALL PROCESS STEPS (CONTEXT ONLY):", prompt);
        Assert.Contains("- Bước 1: Chiết xuất 40ml espresso.", prompt);
        Assert.Contains("- Bước 2: Đánh nóng sữa tươi tạo bọt mịn.", prompt);
        Assert.Contains("- Bước 3: Rót sữa vào espresso tạo hình latte art.", prompt);
        Assert.Contains("GENERAL RECIPE NOTE: Phục vụ nóng trong tách gốm sứ trắng", prompt);
        Assert.Contains("USER VISUAL NOTE: Tách đặt trên đĩa lót", prompt);
        Assert.DoesNotContain("CURRENT STEP", prompt);
    }

    [Fact]
    public void AttachCanonicalStyle_applies_correct_style_for_step_and_final_product()
    {
        var englishText = "Barista pouring milk foam into a cup";

        var stepPrompt = _builder.AttachCanonicalStyle(AiDraftTargetKind.StepInstruction, englishText);
        Assert.Contains(englishText, stepPrompt);
        Assert.Contains("Clean commercial beverage process photography", stepPrompt);
        Assert.Contains("Only bar professional hands or forearms visible", stepPrompt);
        Assert.DoesNotContain("Commercial food and beverage showcase photography", stepPrompt);

        var finalPrompt = _builder.AttachCanonicalStyle(AiDraftTargetKind.FinalProduct, englishText);
        Assert.Contains(englishText, finalPrompt);
        Assert.Contains("Commercial food and beverage showcase photography", finalPrompt);
        Assert.Contains("No human hands, no people", finalPrompt);
        Assert.DoesNotContain("Clean commercial beverage process photography", finalPrompt);
    }

    [Fact]
    public void AttachCanonicalStyle_throws_when_prompt_exceeds_8000_chars()
    {
        var massiveText = new string('a', 8000);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _builder.AttachCanonicalStyle(AiDraftTargetKind.FinalProduct, massiveText));

        Assert.Contains("giới hạn cho phép", ex.Message);
    }
}
