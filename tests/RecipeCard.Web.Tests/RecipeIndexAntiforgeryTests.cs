using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Tests;

public sealed partial class RecipeIndexAntiforgeryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RecipeIndexAntiforgeryTests(WebApplicationFactory<Program> factory)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"AntiforgeryTestDb_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<RecipeDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<RecipeDbContext>(options =>
                {
                    options.UseSqlite(connectionString);
                });
            });
        });
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected_with_400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SelectedRecipeIds"] = "1"
        });

        var response = await client.PostAsync("/Recipes?handler=ExportSelected", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_valid_token_and_valid_id_exports_pdf_with_200()
    {
        var client = _factory.CreateClient();

        // 1. GET page to receive antiforgery token & cookie
        var getResponse = await client.GetAsync("/Recipes");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();

        var token = ExtractAntiforgeryToken(html);
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Recipe Id 1 is seeded by Program.cs on startup
        var postData = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["SelectedRecipeIds"] = "1"
        };

        var postResponse = await client.PostAsync("/Recipes?handler=ExportSelected", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.Equal("application/pdf", postResponse.Content.Headers.ContentType?.MediaType);
        var bytes = await postResponse.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);

        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.True(pdf.NumberOfPages >= 3);
    }

    [Fact]
    public async Task Post_with_non_numeric_ids_returns_400_with_model_error()
    {
        var client = _factory.CreateClient();
        var getResponse = await client.GetAsync("/Recipes");
        var html = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);

        var postData = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("SelectedRecipeIds", "invalid_text")
        };

        var postResponse = await client.PostAsync("/Recipes?handler=ExportSelected", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);
        var postHtml = await postResponse.Content.ReadAsStringAsync();
        var decodedHtml = System.Net.WebUtility.HtmlDecode(postHtml);
        Assert.Contains("Dữ liệu lựa chọn công thức không hợp lệ.", decodedHtml);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryRegex().Match(html);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""")]
    private static partial Regex AntiforgeryRegex();
}
