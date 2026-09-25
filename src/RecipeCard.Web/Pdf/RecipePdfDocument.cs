using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RecipeCard.Web.Pdf;

public sealed class RecipePdfModel
{
    public string Title { get; set; } = string.Empty;
    public string? GeneralNote { get; set; }
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
    private readonly RecipePdfModel _model = model;

    // Design Tokens (aligned with doc/DESIGN.md)
    private const string ColorInk = "#141414";
    private const string ColorInkSoft = "#262626";
    private const string ColorTextMuted = "#707070";
    private const string ColorCanvasSoft = "#F3F3F3";
    private const string ColorHairline = "#E0E0E0";
    private const string ColorHairlineSoft = "#F0F0F0";

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(25);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(ColorInk));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(titleCol =>
                {
                    titleCol.Item().Text(_model.Title)
                        .FontSize(20)
                        .Bold()
                        .FontColor(ColorInk);

                    titleCol.Item().Text("BẢNG CÔNG THỨC PHA CHẾ & HƯỚNG DẪN THỰC HIỆN")
                        .FontSize(9)
                        .Bold()
                        .FontColor(ColorTextMuted);
                });

                row.ConstantItem(120).AlignRight().Text($"Ngày in: {DateTime.Now:dd/MM/yyyy}")
                    .FontSize(8)
                    .FontColor(ColorTextMuted);
            });

            col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor(ColorInk);
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.PaddingVertical(10).Column(col =>
        {
            // Section 1: Ingredients table
            col.Item().Text("1. THÀNH PHẦN NGUYÊN LIỆU")
                .FontSize(11)
                .Bold()
                .FontColor(ColorInk);

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(40);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1);
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Element(HeaderStyle).AlignCenter().Text("STT");
                    header.Cell().Element(HeaderStyle).Text("Tên nguyên liệu");
                    header.Cell().Element(HeaderStyle).AlignRight().Text("Định lượng");
                    header.Cell().Element(HeaderStyle).AlignCenter().Text("Đơn vị");

                    static IContainer HeaderStyle(IContainer cell) =>
                        cell.Background(ColorCanvasSoft)
                            .Border(0.5f)
                            .BorderColor(ColorHairline)
                            .Padding(6)
                            .DefaultTextStyle(x => x.Bold().FontSize(9).FontColor(ColorInk));
                });

                // Rows
                foreach (var line in _model.Ingredients)
                {
                    var isEven = line.Stt % 2 == 0;
                    var bg = isEven ? ColorCanvasSoft : "#FFFFFF";

                    table.Cell().Element(c => CellStyle(c, bg)).AlignCenter().Text(line.Stt.ToString());
                    table.Cell().Element(c => CellStyle(c, bg)).Text(line.Name).SemiBold();
                    table.Cell().Element(c => CellStyle(c, bg)).AlignRight().Text(line.Quantity.ToString("0.##")).Bold();
                    table.Cell().Element(c => CellStyle(c, bg)).AlignCenter().Text(line.Unit);

                    static IContainer CellStyle(IContainer cell, string background) =>
                        cell.Background(background)
                            .Border(0.5f)
                            .BorderColor(ColorHairlineSoft)
                            .Padding(5)
                            .DefaultTextStyle(x => x.FontSize(9).FontColor(ColorInkSoft));
                }
            });

            // Section 2: Steps
            col.Item().PaddingTop(16).Text("2. QUY TRÌNH THỰC HIỆN")
                .FontSize(11)
                .Bold()
                .FontColor(ColorInk);

            col.Item().PaddingTop(6).Column(stepsCol =>
            {
                foreach (var step in _model.Steps)
                {
                    stepsCol.Item().PaddingBottom(8).Border(0.5f).BorderColor(ColorHairline).Background(Colors.White).Padding(8).Column(stepCard =>
                    {
                        stepCard.Item().Row(r =>
                        {
                            r.AutoItem().Background(ColorInk).PaddingHorizontal(8).PaddingVertical(3).Text($"BƯỚC {step.StepNumber}")
                                .FontSize(9).Bold().FontColor(Colors.White);

                            r.RelativeItem().PaddingLeft(10).AlignMiddle().Text(step.Instruction)
                                .FontSize(9.5f).FontColor(ColorInkSoft);
                        });

                        if (step.ImageBytes != null && step.ImageBytes.Length > 0)
                        {
                            try
                            {
                                stepCard.Item().PaddingTop(8).PaddingLeft(65).Column(imgCol =>
                                {
                                    imgCol.Item().MaxHeight(140).Image(step.ImageBytes);
                                    if (step.IsAiIllustration)
                                    {
                                        imgCol.Item().PaddingTop(2).Text("— Hình ảnh minh họa AI").FontSize(7.5f).Italic().FontColor(ColorTextMuted);
                                    }
                                });
                            }
                            catch
                            {
                                // Graceful fallback
                            }
                        }
                        else if (!string.IsNullOrEmpty(step.ImageFullPath) && File.Exists(step.ImageFullPath))
                        {
                            try
                            {
                                stepCard.Item().PaddingTop(8).PaddingLeft(65).MaxHeight(140).Image(step.ImageFullPath);
                            }
                            catch
                            {
                                // Graceful fallback if image decoding fails
                            }
                        }
                    });
                }
            });

            // Section 3: General Note
            if (!string.IsNullOrWhiteSpace(_model.GeneralNote))
            {
                col.Item().PaddingTop(12).Text("3. LƯU Ý & HƯỚNG DẪN PHỤC VỤ")
                    .FontSize(11)
                    .Bold()
                    .FontColor(ColorInk);

                col.Item().PaddingTop(4).Border(0.5f).BorderColor(ColorHairline).Background(ColorCanvasSoft).Padding(8).Text(_model.GeneralNote)
                    .FontSize(9).Italic().FontColor(ColorInkSoft);
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(ColorHairline);
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
