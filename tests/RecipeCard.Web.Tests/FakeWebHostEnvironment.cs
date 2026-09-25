using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace RecipeCard.Web.Tests;

internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "RecipeCard.Web";
}
