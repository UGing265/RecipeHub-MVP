using QuestPDF.Fluent;
using RecipeCard.Web.Pdf;
using Xunit;

namespace RecipeCard.Web.Tests;

public class RecipePdfHeroImageTests
{
    private static readonly byte[] ValidDecodeableJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCABkAGQDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigAooooAKKKKACiiigAooooAKKKKACiiigD//2Q==");

    [Fact]
    public void GeneratePdf_with_hero_image_succeeds_and_produces_pdf_bytes()
    {
        var model = new RecipePdfModel
        {
            Title = "Trà Sữa Oolong Nướng",
            GeneralNote = "Uống lạnh kèm trân châu đen",
            HeroImageBytes = ValidDecodeableJpeg,
            Ingredients =
            [
                new RecipePdfIngredientLine { Stt = 1, Name = "Cốt trà oolong", Quantity = 120, Unit = "ml" },
                new RecipePdfIngredientLine { Stt = 2, Name = "Bột sữa", Quantity = 35, Unit = "g" }
            ],
            Steps =
            [
                new RecipePdfStepLine { StepNumber = 1, Instruction = "Khuấy tan bột sữa vào cốt trà nóng." }
            ]
        };

        var doc = new RecipePdfDocument(model);
        var bytes = doc.GeneratePdf();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void GeneratePdf_without_hero_image_renders_clean_placeholder_rail()
    {
        var model = new RecipePdfModel
        {
            Title = "Trà Chanh Giã Tay",
            HeroImageBytes = null,
            Ingredients =
            [
                new RecipePdfIngredientLine { Stt = 1, Name = "Chanh Quảng Đông", Quantity = 1, Unit = "quả" }
            ],
            Steps =
            [
                new RecipePdfStepLine { StepNumber = 1, Instruction = "Cắt lát chanh rồi giã mạnh tay cùng đá." }
            ]
        };

        var doc = new RecipePdfDocument(model);
        var bytes = doc.GeneratePdf();

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);
    }
}
