using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RecipeCard.Web.Pdf;

public sealed record RecipeBookletPdfModel(
    IReadOnlyList<RecipePdfModel> Recipes,
    DateTimeOffset ExportedAt,
    string OfficialLogoSvgPath);

public sealed class RecipeBookletPdfDocument : IDocument
{
    private readonly RecipeBookletPdfModel _model;
    private readonly string _logoSvgContent;
    private const int MaxItemsPerTocPage = 15;

    public RecipeBookletPdfDocument(RecipeBookletPdfModel model)
    {
        if (model.Recipes == null || model.Recipes.Count == 0 || model.Recipes.Count > 50)
        {
            throw new ArgumentException("Danh sách công thức phải chứa từ 1 đến 50 công thức.", nameof(model));
        }

        foreach (var r in model.Recipes)
        {
            if (string.IsNullOrWhiteSpace(r.Title) || r.Ingredients.Count == 0 || r.Steps.Count == 0)
            {
                throw new InvalidOperationException($"Công thức '{r.Title}' chưa đủ điều kiện xuất PDF.");
            }
        }

        if (string.IsNullOrWhiteSpace(model.OfficialLogoSvgPath) ||
            !model.OfficialLogoSvgPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(model.OfficialLogoSvgPath))
        {
            throw new InvalidOperationException(
                $"Không tìm thấy asset logo chính thức tại '{model.OfficialLogoSvgPath}'. " +
                "Yêu cầu file SVG chính thức của thương hiệu để xuất booklet.");
        }

        try
        {
            _logoSvgContent = File.ReadAllText(model.OfficialLogoSvgPath);
            if (string.IsNullOrWhiteSpace(_logoSvgContent))
            {
                throw new InvalidOperationException("Nội dung file logo SVG không hợp lệ (rỗng).");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Lỗi khi đọc file logo SVG tại '{model.OfficialLogoSvgPath}': {ex.Message}", ex);
        }

        _model = model;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = "Bộ hướng dẫn pha chế sản phẩm",
        Author = "R&D Recipe Hub",
        Creator = "R&D Recipe Hub",
        Subject = $"{_model.Recipes.Count} công thức",
        CreationDate = _model.ExportedAt.DateTime
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        RecipePdfConstants.EnsureFontsRegistered();

        // Page 1: Cover
        container.Page(ComposeCoverPage);

        // Page 2+: Table of Contents
        var totalTocPages = (int)Math.Ceiling(_model.Recipes.Count / (double)MaxItemsPerTocPage);
        for (int p = 0; p < totalTocPages; p++)
        {
            int pageIndex = p;
            var chunk = _model.Recipes.Skip(pageIndex * MaxItemsPerTocPage).Take(MaxItemsPerTocPage).ToList();
            int startIndex = pageIndex * MaxItemsPerTocPage;
            container.Page(page => ComposeTocPage(page, chunk, startIndex, pageIndex, totalTocPages));
        }

        // Subsequent Pages: Individual recipes
        foreach (var recipe in _model.Recipes)
        {
            var composer = new RecipePdfPageComposer(recipe);
            container.Page(composer.Configure);
        }
    }

    private void ComposeCoverPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(0);
        page.DefaultTextStyle(x => x.FontFamily(RecipePdfConstants.FontFamily).FontColor(RecipePdfConstants.ColorInk));

        page.Content().Row(row =>
        {
            // Left Column: 58% width, ấm màu trà Phê La với chữ trắng
            row.RelativeItem(0.58f).Background(RecipePdfConstants.ColorInk).Padding(48).Column(col =>
            {
                col.Item().Text("BỘ HƯỚNG DẪN")
                    .FontSize(18).Bold().LetterSpacing(0.05f).FontColor(Colors.White);

                col.Item().PaddingTop(8).Text("PHA CHẾ SẢN PHẨM")
                    .FontSize(32).ExtraBold().LetterSpacing(-0.01f).FontColor(Colors.White);

                col.Item().PaddingTop(16).LineHorizontal(1).LineColor(RecipePdfConstants.ColorAccent);

                col.Item().PaddingTop(180).Text("Phòng đào tạo - Phê La")
                    .FontSize(11).SemiBold().FontColor(Colors.White);

                col.Item().PaddingTop(4).Text($"Xuất bởi R&D Recipe Hub • {_model.ExportedAt:dd/MM/yyyy} • {_model.Recipes.Count} công thức")
                    .FontSize(9).FontColor(RecipePdfConstants.ColorAccentSoft);
            });

            // Right Column: 42% width, nền Canvas Soft ấm chứa logo SVG
            row.RelativeItem(0.42f).Background(RecipePdfConstants.ColorCanvasSoft).Padding(48).AlignCenter().AlignMiddle().Column(col =>
            {
                col.Item().Height(240).Svg(_logoSvgContent).FitArea();
            });
        });
    }

    private void ComposeTocPage(
        PageDescriptor page,
        IReadOnlyList<RecipePdfModel> chunk,
        int startIndex,
        int pageIndex,
        int totalTocPages)
    {
        page.Size(PageSizes.A4.Landscape());
        page.MarginHorizontal(36);
        page.MarginVertical(28);
        page.DefaultTextStyle(x => x.FontFamily(RecipePdfConstants.FontFamily).FontSize(9).FontColor(RecipePdfConstants.ColorInk));

        page.Header().Height(40).Row(row =>
        {
            row.RelativeItem().Text(totalTocPages > 1 ? $"MỤC LỤC ({pageIndex + 1}/{totalTocPages})" : "MỤC LỤC")
                .FontSize(20).Bold().FontColor(RecipePdfConstants.ColorAccent);

            row.ConstantItem(140).AlignRight().AlignMiddle()
                .Text($"{_model.Recipes.Count} CÔNG THỨC").FontSize(9).SemiBold().FontColor(RecipePdfConstants.ColorTextMuted);
        });

        page.Content().PaddingTop(10).Column(col =>
        {
            col.Item().LineHorizontal(1).LineColor(RecipePdfConstants.ColorHairline);

            col.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(40);
                    cols.RelativeColumn();
                });

                for (int i = 0; i < chunk.Count; i++)
                {
                    var index = startIndex + i + 1;
                    var recipe = chunk[i];

                    table.Cell().BorderBottom(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft).PaddingVertical(5)
                        .Text($"{index:00}.").FontSize(9).Bold().FontColor(RecipePdfConstants.ColorAccent);

                    table.Cell().BorderBottom(0.5f).BorderColor(RecipePdfConstants.ColorHairlineSoft).PaddingVertical(5)
                        .Text(recipe.Title).FontSize(9.5f).SemiBold().FontColor(RecipePdfConstants.ColorInkSoft);
                }
            });
        });

        page.Footer().Height(24).Row(row =>
        {
            row.RelativeItem().AlignMiddle()
                .Text("R&D Recipe Hub").FontSize(8).Italic().FontColor(RecipePdfConstants.ColorTextMuted);

            row.ConstantItem(60).AlignRight().AlignMiddle().Text(text =>
            {
                text.CurrentPageNumber().FontSize(8).FontColor(RecipePdfConstants.ColorTextMuted);
                text.Span(" / ").FontSize(8).FontColor(RecipePdfConstants.ColorTextMuted);
                text.TotalPages().FontSize(8).FontColor(RecipePdfConstants.ColorTextMuted);
            });
        });
    }
}
