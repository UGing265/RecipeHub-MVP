using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RecipeCard.Web.Pdf;

public static class RecipePdfConstants
{
    public const string FontFamily = "Be Vietnam Pro";

    // Phê La Warm Tea Palette (Màu trà/nâu đất đặc trưng gốc)
    public const string ColorInk = "#3B2A1F";
    public const string ColorInkSoft = "#5C4432";
    public const string ColorTextMuted = "#806A59";
    public const string ColorAccent = "#9A5B20";
    public const string ColorAccentSoft = "#E9C9A5";
    public const string ColorCanvas = "#FFFFFF";
    public const string ColorCanvasSoft = "#FBF7F1";
    public const string ColorHairline = "#B58A62";
    public const string ColorHairlineSoft = "#E9D8C8";

    private static readonly object FontRegistrationLock = new();
    private static bool _fontsRegistered;

    public static void EnsureFontsRegistered()
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

            var assembly = typeof(RecipePdfConstants).Assembly;
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-Regular.ttf");
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-SemiBold.ttf");
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-Bold.ttf");
            FontManager.RegisterFontFromEmbeddedResource(assembly, "RecipeCard.Web.Pdf.Fonts.BeVietnamPro-Italic.ttf");
            _fontsRegistered = true;
        }
    }
}

internal sealed class RecipePdfPageComposer(RecipePdfModel model)
{
    private enum StepLayout
    {
        Detailed,
        Grid,
        Compact
    }

    private readonly RecipePdfModel _model = model;

    public void Configure(PageDescriptor page)
    {
        RecipePdfConstants.EnsureFontsRegistered();

        page.Size(PageSizes.A4.Landscape());
        page.MarginHorizontal(26);
        page.MarginVertical(20);
        page.DefaultTextStyle(x => x.FontFamily(RecipePdfConstants.FontFamily).FontSize(9).FontColor(RecipePdfConstants.ColorInk));

        page.Header().Element(ComposeHeader);
        page.Content().Element(ComposeContent);
        page.Footer().Element(ComposeFooter);
    }

    private void ComposeHeader(IContainer container)
    {
        container.MinHeight(42).Background(RecipePdfConstants.ColorAccentSoft).Row(row =>
        {
            row.ConstantItem(36).Background(Colors.White).AlignCenter().AlignMiddle()
                .Text("P").FontSize(18).Bold().FontColor(RecipePdfConstants.ColorInk);
            row.RelativeItem().PaddingLeft(14).PaddingRight(8).AlignMiddle().Text(_model.Title)
                .FontSize(18).Bold().LineHeight(1.15f).FontColor(RecipePdfConstants.ColorAccent);
            row.ConstantItem(120).Background(RecipePdfConstants.ColorAccent);
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
                column.Item().Height(105).Background(RecipePdfConstants.ColorCanvasSoft).AlignCenter().AlignMiddle()
                    .Text(text =>
                    {
                        text.Span("P").FontSize(38).Bold().FontColor(RecipePdfConstants.ColorAccent);
                        text.Span("\nRECIPE").FontSize(8).SemiBold().FontColor(RecipePdfConstants.ColorTextMuted);
                    });
            }

            column.Item().PaddingTop(14).Background(RecipePdfConstants.ColorCanvasSoft).PaddingVertical(12).AlignCenter()
                .Text("CÔNG THỨC\nVẬN HÀNH").FontSize(10).Bold().LineHeight(1.25f).FontColor(RecipePdfConstants.ColorInk);
            column.Item().PaddingTop(18).AlignCenter().Text("R&D Recipe Hub")
                .FontSize(8).Italic().FontColor(RecipePdfConstants.ColorTextMuted);
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
        container.Border(1).BorderColor(RecipePdfConstants.ColorHairline).Column(column =>
        {
            column.Item().Background(RecipePdfConstants.ColorCanvasSoft).BorderBottom(1).BorderColor(RecipePdfConstants.ColorHairline)
                .PaddingVertical(6).AlignCenter().Text("THÀNH PHẦN / ĐỊNH LƯỢNG")
                .FontSize(9.5f).Bold().FontColor(RecipePdfConstants.ColorAccent);
            column.Item().Element(ComposeIngredients);
            column.Item().BorderTop(1).BorderColor(RecipePdfConstants.ColorHairline).Element(ComposeProcedureRow);

            if (!string.IsNullOrWhiteSpace(_model.GeneralNote))
            {
                column.Item().BorderTop(1).BorderColor(RecipePdfConstants.ColorHairline).Row(row =>
                {
                    row.ConstantItem(115).Background(RecipePdfConstants.ColorCanvasSoft).Padding(8).AlignMiddle()
                        .Text("Lưu ý").FontSize(9).SemiBold().FontColor(RecipePdfConstants.ColorAccent);
                    row.RelativeItem().BorderLeft(1).BorderColor(RecipePdfConstants.ColorHairline).Padding(8)
                        .Text(_model.GeneralNote).FontSize(8.5f).Italic().LineHeight(1.25f).FontColor(RecipePdfConstants.ColorInkSoft);
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
                table.Cell().BorderBottom(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft).PaddingHorizontal(7).PaddingVertical(5.5f)
                    .Text(ingredient.Name).FontSize(8.5f).SemiBold().FontColor(RecipePdfConstants.ColorInkSoft);
                table.Cell().BorderLeft(1).BorderBottom(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft)
                    .PaddingHorizontal(7).PaddingVertical(5.5f).AlignCenter()
                    .Text($"{ingredient.Quantity:0.##} {ingredient.Unit}").FontSize(8.5f).FontColor(RecipePdfConstants.ColorInk);
            }
        });
    }

    private void ComposeProcedureRow(IContainer container)
    {
        container.Row(row =>
        {
            row.ConstantItem(115).Background(RecipePdfConstants.ColorCanvasSoft).Padding(8).AlignMiddle()
                .Text("Các bước thực hiện").FontSize(9).SemiBold().FontColor(RecipePdfConstants.ColorAccent);
            row.RelativeItem().BorderLeft(1).BorderColor(RecipePdfConstants.ColorHairline).Padding(7).Element(ComposeSteps);
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
                column.Item().ShowEntire().PaddingBottom(10).Border(0.75f).BorderColor(RecipePdfConstants.ColorHairline).Padding(10).Row(row =>
                {
                    if (HasImage(step))
                    {
                        row.RelativeItem(0.42f).Element(image => ComposeStepImage(image, step, 135));
                    }

                    row.RelativeItem(0.58f).PaddingLeft(HasImage(step) ? 12 : 0).Column(content =>
                    {
                        content.Item().Element(label => ComposeStepLabel(label, step.StepNumber));
                        content.Item().PaddingTop(9).Text(step.Instruction)
                            .FontSize(10).LineHeight(1.3f).FontColor(RecipePdfConstants.ColorInkSoft);
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
                column.Item().ShowEntire().MinHeight(62).BorderBottom(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft)
                    .PaddingVertical(7).Row(row =>
                {
                    row.ConstantItem(28).AlignTop().Text($"{step.StepNumber}.")
                        .FontSize(8.5f).Bold().FontColor(RecipePdfConstants.ColorAccent);
                    if (HasImage(step))
                    {
                        row.ConstantItem(78).PaddingRight(10).Element(image => ComposeStepImage(image, step, 68));
                    }
                    row.RelativeItem().Text(step.Instruction)
                        .FontSize(8.2f).LineHeight(1.2f).FontColor(RecipePdfConstants.ColorInkSoft);
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
                column.Item().ShowEntire().PaddingBottom(4).BorderBottom(0.5f).BorderColor(RecipePdfConstants.ColorHairline).PaddingVertical(5).Row(row =>
                {
                    row.ConstantItem(58).Element(label => ComposeCompactStepLabel(label, step.StepNumber));
                    if (HasImage(step))
                    {
                        row.ConstantItem(84).PaddingLeft(8).Element(image => ComposeStepImage(image, step, 48));
                    }
                    row.RelativeItem().PaddingLeft(9).AlignMiddle().Text(step.Instruction)
                        .FontSize(8).LineHeight(1.18f).FontColor(RecipePdfConstants.ColorInkSoft);
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
            row.AutoItem().Background(RecipePdfConstants.ColorInk).PaddingHorizontal(7).PaddingVertical(3)
                .Text(stepNumber.ToString("00")).FontSize(8).Bold().FontColor(Colors.White);
            row.RelativeItem().AlignMiddle().PaddingLeft(7).Text($"BƯỚC {stepNumber}")
                .FontSize(8).Bold().FontColor(RecipePdfConstants.ColorInk);
        });
    }

    private static void ComposeCompactStepLabel(IContainer container, int stepNumber)
    {
        container.Background(RecipePdfConstants.ColorInk).PaddingHorizontal(7).PaddingVertical(4).AlignCenter()
            .Text($"BƯỚC {stepNumber:00}").FontSize(7.5f).Bold().FontColor(Colors.White);
    }

    private static void ComposeStepImage(IContainer container, RecipePdfStepLine step, float height)
    {
        container.Column(column =>
        {
            if (step.ImageBytes is { Length: > 0 })
            {
                column.Item().Height(height).Background(Colors.White).Border(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft)
                    .Padding(2).Image(step.ImageBytes).FitArea();
            }
            else if (!string.IsNullOrWhiteSpace(step.ImageFullPath) && File.Exists(step.ImageFullPath))
            {
                column.Item().Height(height).Background(Colors.White).Border(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft)
                    .Padding(2).Image(step.ImageFullPath).FitArea();
            }
            else
            {
                throw new FileNotFoundException("Không tìm thấy ảnh của bước công thức.", step.ImageFullPath);
            }

            if (step.IsAiIllustration)
            {
                column.Item().PaddingTop(2).Text("Hình ảnh minh họa AI")
                    .FontSize(7.5f).Italic().FontColor(RecipePdfConstants.ColorTextMuted);
            }
        });
    }

    private static bool HasImage(RecipePdfStepLine step) =>
        step.ImageBytes is { Length: > 0 }
        || (!string.IsNullOrWhiteSpace(step.ImageFullPath) && File.Exists(step.ImageFullPath));

    private static bool HasHeroImage(RecipePdfModel model) =>
        model.HeroImageBytes is { Length: > 0 }
        || (!string.IsNullOrWhiteSpace(model.HeroImageFullPath) && File.Exists(model.HeroImageFullPath));

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(RecipePdfConstants.ColorHairlineSoft);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("R&D Recipe Hub - Tài liệu vận hành & đào tạo nội bộ")
                    .FontSize(8)
                    .FontColor(RecipePdfConstants.ColorTextMuted);

                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.CurrentPageNumber().FontSize(8).FontColor(RecipePdfConstants.ColorTextMuted);
                    text.Span(" / ").FontSize(8).FontColor(RecipePdfConstants.ColorTextMuted);
                    text.TotalPages().FontSize(8).FontColor(RecipePdfConstants.ColorTextMuted);
                });
            });
        });
    }
}
