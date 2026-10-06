using QuestPDF.Fluent;
using QuestPDF.Drawing;
using QuestPDF.Helpers;
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
    private enum StepLayout
    {
        Detailed,
        Grid,
        Compact
    }

    private readonly RecipePdfModel _model = model;
    private static readonly object FontRegistrationLock = new();
    private static bool _fontsRegistered;

    private const string FontFamily = "Be Vietnam Pro";
    private const string ColorInk = "#3B2A1F";
    private const string ColorInkSoft = "#5C4432";
    private const string ColorTextMuted = "#806A59";
    private const string ColorAccent = "#9A5B20";
    private const string ColorAccentSoft = "#E9C9A5";
    private const string ColorCanvasSoft = "#FBF7F1";
    private const string ColorHairline = "#B58A62";
    private const string ColorHairlineSoft = "#E9D8C8";

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        EnsureFontsRegistered();

        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.MarginHorizontal(26);
            page.MarginVertical(20);
            page.DefaultTextStyle(x => x.FontFamily(FontFamily).FontSize(9).FontColor(ColorInk));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Height(42).Background(ColorAccentSoft).Row(row =>
        {
            row.ConstantItem(36).Background(Colors.White).AlignCenter().AlignMiddle()
                .Text("P").FontSize(18).Bold().FontColor(ColorInk);
            row.RelativeItem().PaddingLeft(14).AlignMiddle().Text(_model.Title)
                .FontSize(20).Bold().FontColor(ColorAccent);
            row.ConstantItem(165).Background(ColorAccent);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingTop(7).Row(row =>
        {
            row.ConstantItem(145).PaddingRight(12).AlignTop().Element(ComposeVisualRail);
            row.RelativeItem().AlignTop().Element(ComposeRecipeTable);
        });
    }

    private void ComposeVisualRail(IContainer container)
    {
        container.Column(column =>
        {
            if (HasHeroImage(_model))
            {
                column.Item().Height(190).Element(ComposeHeroImage);
            }
            else
            {
                column.Item().Height(105).Background(ColorCanvasSoft).AlignCenter().AlignMiddle()
                    .Text(text =>
                    {
                        text.Span("P").FontSize(38).Bold().FontColor(ColorAccent);
                        text.Span("\nRECIPE").FontSize(8).SemiBold().FontColor(ColorTextMuted);
                    });
            }

            column.Item().PaddingTop(14).Background(ColorCanvasSoft).PaddingVertical(12).AlignCenter()
                .Text("CÔNG THỨC\nVẬN HÀNH").FontSize(10).Bold().LineHeight(1.25f).FontColor(ColorInk);
            column.Item().PaddingTop(18).AlignCenter().Text("R&D Recipe Hub")
                .FontSize(8).Italic().FontColor(ColorTextMuted);
        });
    }

    private void ComposeHeroImage(IContainer container)
    {
        if (_model.HeroImageBytes is { Length: > 0 })
        {
            container.Image(_model.HeroImageBytes).FitArea();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_model.HeroImageFullPath) && File.Exists(_model.HeroImageFullPath))
        {
            container.Image(_model.HeroImageFullPath).FitArea();
            return;
        }

        throw new FileNotFoundException("Không tìm thấy ảnh đại diện của công thức.", _model.HeroImageFullPath);
    }

    private void ComposeRecipeTable(IContainer container)
    {
        container.Border(1).BorderColor(ColorHairline).Column(column =>
        {
            column.Item().Background(ColorCanvasSoft).BorderBottom(1).BorderColor(ColorHairline)
                .PaddingVertical(6).AlignCenter().Text("THÀNH PHẦN / ĐỊNH LƯỢNG")
                .FontSize(9.5f).Bold().FontColor(ColorAccent);
            column.Item().Element(ComposeIngredients);
            column.Item().BorderTop(1).BorderColor(ColorHairline).Element(ComposeProcedureRow);

            if (!string.IsNullOrWhiteSpace(_model.GeneralNote))
            {
                column.Item().BorderTop(1).BorderColor(ColorHairline).Row(row =>
                {
                    row.ConstantItem(115).Background(ColorCanvasSoft).Padding(8).AlignMiddle()
                        .Text("Lưu ý").FontSize(9).SemiBold().FontColor(ColorAccent);
                    row.RelativeItem().BorderLeft(1).BorderColor(ColorHairline).Padding(8)
                        .Text(_model.GeneralNote).FontSize(8.5f).Italic().LineHeight(1.25f).FontColor(ColorInkSoft);
                });
            }
        });
    }

    private void ComposeIngredients(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1.25f);
                columns.RelativeColumn(2.75f);
            });

            foreach (var ingredient in _model.Ingredients)
            {
                table.Cell().BorderBottom(0.5f).BorderColor(ColorHairlineSoft).PaddingHorizontal(7).PaddingVertical(5.5f)
                    .Text(ingredient.Name).FontSize(8.5f).SemiBold().FontColor(ColorInkSoft);
                table.Cell().BorderLeft(1).BorderBottom(0.5f).BorderColor(ColorHairlineSoft)
                    .PaddingHorizontal(7).PaddingVertical(5.5f).AlignCenter()
                    .Text($"{ingredient.Quantity:0.##} {ingredient.Unit}").FontSize(8.5f).FontColor(ColorInk);
            }
        });
    }

    private void ComposeProcedureRow(IContainer container)
    {
        container.Row(row =>
        {
            row.ConstantItem(115).Background(ColorCanvasSoft).Padding(8).AlignMiddle()
                .Text("Các bước thực hiện").FontSize(9).SemiBold().FontColor(ColorAccent);
            row.RelativeItem().BorderLeft(1).BorderColor(ColorHairline).Padding(7).Element(ComposeSteps);
        });
    }

    private void ComposeSteps(IContainer container)
    {
        var layout = SelectStepLayout(_model.Steps);
        if (layout == StepLayout.Detailed)
        {
            ComposeDetailedSteps(container);
            return;
        }

        if (layout == StepLayout.Grid)
        {
            ComposeStepGrid(container);
            return;
        }

        ComposeCompactSteps(container);
    }

    private void ComposeDetailedSteps(IContainer container)
    {
        container.Column(column =>
        {
            foreach (var step in _model.Steps)
            {
                column.Item().ShowEntire().PaddingBottom(10).Border(0.75f).BorderColor(ColorHairline).Padding(10).Row(row =>
                {
                    if (HasImage(step))
                    {
                        row.RelativeItem(0.42f).Element(image => ComposeStepImage(image, step, 135));
                    }

                    row.RelativeItem(0.58f).PaddingLeft(HasImage(step) ? 12 : 0).Column(content =>
                    {
                        content.Item().Element(label => ComposeStepLabel(label, step.StepNumber));
                        content.Item().PaddingTop(9).Text(step.Instruction)
                            .FontSize(10).LineHeight(1.3f).FontColor(ColorInkSoft);
                    });
                });
            }
        });
    }

    private void ComposeStepGrid(IContainer container)
    {
        container.Column(column =>
        {
            foreach (var step in _model.Steps)
            {
                column.Item().ShowEntire().MinHeight(62).BorderBottom(0.5f).BorderColor(ColorHairlineSoft)
                    .PaddingVertical(7).Row(row =>
                {
                    row.ConstantItem(28).AlignTop().Text($"{step.StepNumber}.")
                        .FontSize(8.5f).Bold().FontColor(ColorAccent);
                    if (HasImage(step))
                    {
                        row.ConstantItem(78).PaddingRight(10).Element(image => ComposeStepImage(image, step, 68));
                    }
                    row.RelativeItem().Text(step.Instruction)
                        .FontSize(8.2f).LineHeight(1.2f).FontColor(ColorInkSoft);
                });
            }
        });
    }

    private void ComposeCompactSteps(IContainer container)
    {
        container.Column(column =>
        {
            foreach (var step in _model.Steps)
            {
                column.Item().ShowEntire().PaddingBottom(4).BorderBottom(0.5f).BorderColor(ColorHairline).PaddingVertical(5).Row(row =>
                {
                    row.ConstantItem(58).Element(label => ComposeCompactStepLabel(label, step.StepNumber));
                    if (HasImage(step))
                    {
                        row.ConstantItem(84).PaddingLeft(8).Element(image => ComposeStepImage(image, step, 48));
                    }
                    row.RelativeItem().PaddingLeft(9).AlignMiddle().Text(step.Instruction)
                        .FontSize(8).LineHeight(1.18f).FontColor(ColorInkSoft);
                });
            }
        });
    }

    private static StepLayout SelectStepLayout(IReadOnlyCollection<RecipePdfStepLine> steps)
    {
        var totalCharacters = steps.Sum(step => step.Instruction.Length);
        var longestInstruction = steps.Max(step => step.Instruction.Length);

        if (steps.Count <= 2 && totalCharacters <= 700)
        {
            return StepLayout.Detailed;
        }

        if (steps.Count <= 4 && longestInstruction <= 320 && totalCharacters <= 900)
        {
            return StepLayout.Grid;
        }

        return StepLayout.Compact;
    }

    private static void ComposeStepLabel(IContainer container, int stepNumber)
    {
        container.Row(row =>
        {
            row.AutoItem().Background(ColorInk).PaddingHorizontal(7).PaddingVertical(3)
                .Text(stepNumber.ToString("00")).FontSize(8).Bold().FontColor(Colors.White);
            row.RelativeItem().AlignMiddle().PaddingLeft(7).Text($"BƯỚC {stepNumber}")
                .FontSize(8).Bold().FontColor(ColorInk);
        });
    }

    private static void ComposeCompactStepLabel(IContainer container, int stepNumber)
    {
        container.Background(ColorInk).PaddingHorizontal(7).PaddingVertical(4).AlignCenter()
            .Text($"BƯỚC {stepNumber:00}").FontSize(7.5f).Bold().FontColor(Colors.White);
    }

    private static void ComposeStepImage(IContainer container, RecipePdfStepLine step, float height)
    {
        container.Column(column =>
        {
            if (step.ImageBytes is { Length: > 0 })
            {
                column.Item().Height(height).Background(Colors.White).Border(0.5f).BorderColor(ColorHairlineSoft)
                    .Padding(2).Image(step.ImageBytes).FitArea();
            }
            else if (!string.IsNullOrWhiteSpace(step.ImageFullPath) && File.Exists(step.ImageFullPath))
            {
                column.Item().Height(height).Background(Colors.White).Border(0.5f).BorderColor(ColorHairlineSoft)
                    .Padding(2).Image(step.ImageFullPath).FitArea();
            }
            else
            {
                throw new FileNotFoundException("Không tìm thấy ảnh của bước công thức.", step.ImageFullPath);
            }

            if (step.IsAiIllustration)
            {
                column.Item().PaddingTop(2).Text("Hình ảnh minh họa AI")
                    .FontSize(7.5f).Italic().FontColor(ColorTextMuted);
            }
        });
    }

    private static bool HasImage(RecipePdfStepLine step) =>
        step.ImageBytes is { Length: > 0 }
        || (!string.IsNullOrWhiteSpace(step.ImageFullPath) && File.Exists(step.ImageFullPath));

    private static bool HasHeroImage(RecipePdfModel model) =>
        model.HeroImageBytes is { Length: > 0 }
        || (!string.IsNullOrWhiteSpace(model.HeroImageFullPath) && File.Exists(model.HeroImageFullPath));

    private static void EnsureFontsRegistered()
    {
        if (_fontsRegistered)
        {
            return;
        }

        lock (FontRegistrationLock)
        {
            if (_fontsRegistered)
            {
                return;
            }

            var assembly = typeof(RecipePdfDocument).Assembly;
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-Regular.ttf");
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-SemiBold.ttf");
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-Bold.ttf");
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-Italic.ttf");
            _fontsRegistered = true;
        }
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(ColorHairlineSoft);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("R&D Recipe Hub - Tài liệu vận hành & đào tạo nội bộ")
                    .FontSize(8)
                    .FontColor(ColorTextMuted);

                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.CurrentPageNumber().FontSize(8).FontColor(ColorTextMuted);
                    text.Span(" / ").FontSize(8).FontColor(ColorTextMuted);
                    text.TotalPages().FontSize(8).FontColor(ColorTextMuted);
                });
            });
        });
    }
}
