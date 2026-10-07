using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace RecipeCard.Web.Pdf;

public sealed class RecipePdfModel
{
    public string Title { get; set; } = string.Empty;
    public string? GeneralNote { get; set; }
    public byte[]? HeroImageBytes { get; set; }
    public string? HeroImageFullPath { get; set; }
    public List<RecipePdfIngredientLine> Ingredients { get; set; } = [];
    public List<RecipePdfStepLine> Steps { get; set; } = [];
}

public sealed class RecipePdfIngredientLine
{
    public int Stt { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public sealed class RecipePdfStepLine
{
    public int StepNumber { get; set; }
    public string Instruction { get; set; } = string.Empty;
    public string? ImageFullPath { get; set; }
    public byte[]? ImageBytes { get; set; }
    public bool IsAiIllustration { get; set; }
}

public sealed class RecipePdfDocument(RecipePdfModel model) : IDocument
{
    private readonly RecipePdfPageComposer _composer = new(model);

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(_composer.Configure);
    }
}
