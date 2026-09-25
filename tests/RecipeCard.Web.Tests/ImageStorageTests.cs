using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RecipeCard.Web.Services;
using Xunit;

namespace RecipeCard.Web.Tests;

public class ImageStorageTests
{
    private readonly ImageValidator _validator = new();

    private static readonly byte[] ValidJpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ValidWebpBytes = [
        0x52, 0x49, 0x46, 0x46, // RIFF
        0x00, 0x00, 0x00, 0x00, // file size
        0x57, 0x45, 0x42, 0x50, // WEBP
        0x56, 0x50, 0x38, 0x20  // VP8
    ];

    [Fact]
    public void Validator_accepts_valid_jpeg_png_webp()
    {
        var jpegInfo = _validator.ValidateBytes(ValidJpegBytes, "photo.jpg");
        Assert.Equal("image/jpeg", jpegInfo.MimeType);
        Assert.Equal(".jpg", jpegInfo.Extension);

        var pngInfo = _validator.ValidateBytes(ValidPngBytes, "diagram.png");
        Assert.Equal("image/png", pngInfo.MimeType);
        Assert.Equal(".png", pngInfo.Extension);

        var webpInfo = _validator.ValidateBytes(ValidWebpBytes, "image.webp");
        Assert.Equal("image/webp", webpInfo.MimeType);
        Assert.Equal(".webp", webpInfo.Extension);
    }

    [Fact]
    public void Validator_rejects_empty_and_oversized_files()
    {
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateBytes([]));

        var oversized = new byte[ImageValidator.MaxFileSize + 1];
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateBytes(oversized));
    }

    [Fact]
    public void Validator_rejects_mismatched_magic_bytes_or_extension()
    {
        // Text bytes pretending to be a JPG
        var fakeJpg = Encoding.UTF8.GetBytes("not really an image file content");
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateBytes(fakeJpg, "fake.jpg"));

        // Valid JPEG bytes with .exe extension
        Assert.Throws<InvalidOperationException>(() => _validator.ValidateBytes(ValidJpegBytes, "malicious.exe"));
    }

    [Fact]
    public void Validator_works_with_IFormFile()
    {
        var formFile = new FormFile(new MemoryStream(ValidPngBytes), 0, ValidPngBytes.Length, "image", "test.png");
        var info = _validator.Validate(formFile);

        Assert.Equal(".png", info.Extension);
        Assert.Equal("image/png", info.MimeType);
        Assert.Equal(ValidPngBytes.Length, info.ByteSize);
        Assert.Equal("test.png", info.OriginalFileName);
    }

    [Fact]
    public void CloudinaryOptions_validates_required_fields()
    {
        var options = new CloudinaryOptions();
        Assert.Throws<InvalidOperationException>(() => options.Validate());

        options.CloudName = "test-cloud";
        Assert.Throws<InvalidOperationException>(() => options.Validate());

        options.ApiKey = "12345";
        Assert.Throws<InvalidOperationException>(() => options.Validate());

        options.ApiSecret = "secret";
        // Now it should pass
        options.Validate();
    }

    [Fact]
    public void CloudflareOptions_validates_required_fields()
    {
        var options = new CloudflareOptions();
        Assert.Throws<InvalidOperationException>(() => options.Validate());

        options.AccountId = "acc-123";
        Assert.Throws<InvalidOperationException>(() => options.Validate());

        options.ApiToken = "token-abc";
        options.Validate();
    }
}
