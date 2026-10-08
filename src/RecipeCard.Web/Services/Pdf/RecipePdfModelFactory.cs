using Microsoft.Extensions.Logging;
using RecipeCard.Web.Models;
using RecipeCard.Web.Pdf;

namespace RecipeCard.Web.Services;

public interface IRecipePdfModelFactory
{
    Task<RecipePdfModelBatch> CreateAsync(
        IReadOnlyList<Recipe> recipes,
        CancellationToken cancellationToken);
}

public sealed class RecipePdfModelBatch : IAsyncDisposable
{
    public IReadOnlyList<RecipePdfModel> Models { get; }
    public string? TempDirectory { get; }

    public RecipePdfModelBatch(IReadOnlyList<RecipePdfModel> models, string? tempDirectory)
    {
        Models = models;
        TempDirectory = tempDirectory;
    }

    public ValueTask DisposeAsync()
    {
        if (!string.IsNullOrEmpty(TempDirectory) && Directory.Exists(TempDirectory))
        {
            try
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup error
            }
        }

        return ValueTask.CompletedTask;
    }
}

public sealed class RecipePdfModelFactory : IRecipePdfModelFactory
{
    private readonly PdfMediaLoader _mediaLoader;
    private readonly ILogger<RecipePdfModelFactory> _logger;

    public RecipePdfModelFactory(PdfMediaLoader mediaLoader, ILogger<RecipePdfModelFactory> logger)
    {
        _mediaLoader = mediaLoader;
        _logger = logger;
    }

    public async Task<RecipePdfModelBatch> CreateAsync(
        IReadOnlyList<Recipe> recipes,
        CancellationToken cancellationToken)
    {
        if (recipes.Count == 0)
        {
            return new RecipePdfModelBatch([], null);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "recipe-card-pdf", Guid.NewGuid().ToString("N"));

        try
        {
            // Collect media requests
            var mediaRequests = new List<PdfMediaRequest>();

            foreach (var recipe in recipes)
            {
                if (recipe.FinalMediaAsset != null && !string.IsNullOrWhiteSpace(recipe.FinalMediaAsset.DeliveryUrl))
                {
                    mediaRequests.Add(new PdfMediaRequest(
                        Key: $"hero_{recipe.Id}",
                        RecipeId: recipe.Id,
                        Role: PdfMediaRole.Hero,
                        SortOrder: 0,
                        RemoteUrl: recipe.FinalMediaAsset.DeliveryUrl,
                        LocalFileName: null));
                }

                foreach (var step in recipe.Steps.OrderBy(s => s.SortOrder))
                {
                    string? remoteUrl = null;
                    string? localFileName = null;

                    if (step.MediaAsset != null && !string.IsNullOrWhiteSpace(step.MediaAsset.DeliveryUrl))
                    {
                        remoteUrl = step.MediaAsset.DeliveryUrl;
                    }
                    else if (!string.IsNullOrWhiteSpace(step.ImageFileName))
                    {
                        localFileName = step.ImageFileName;
                    }

                    if (remoteUrl != null || localFileName != null)
                    {
                        mediaRequests.Add(new PdfMediaRequest(
                            Key: $"step_{recipe.Id}_{step.Id}",
                            RecipeId: recipe.Id,
                            Role: PdfMediaRole.Step,
                            SortOrder: step.SortOrder,
                            RemoteUrl: remoteUrl,
                            LocalFileName: localFileName));
                    }
                }
            }

            var mediaResults = await _mediaLoader.LoadBatchAsync(mediaRequests, tempDirectory, cancellationToken);

            var models = new List<RecipePdfModel>(recipes.Count);

            foreach (var recipe in recipes)
            {
                var pdfModel = new RecipePdfModel
                {
                    Title = recipe.Name,
                    GeneralNote = recipe.GeneralNote,
                    Ingredients = recipe.Ingredients
                        .Select((ri, idx) => new RecipePdfIngredientLine
                        {
                            Stt = idx + 1,
                            Name = ri.Ingredient?.Name ?? string.Empty,
                            Quantity = ri.Quantity,
                            Unit = ri.Ingredient?.DefaultUnit ?? string.Empty
                        }).ToList()
                };

                // Attach hero image if available
                var heroKey = $"hero_{recipe.Id}";
                if (mediaResults.TryGetValue(heroKey, out var heroPath) && heroPath != null)
                {
                    pdfModel.HeroImageFullPath = heroPath;
                }

                // Attach steps
                var stepLines = new List<RecipePdfStepLine>();
                foreach (var s in recipe.Steps.OrderBy(s => s.SortOrder))
                {
                    var line = new RecipePdfStepLine
                    {
                        StepNumber = s.SortOrder,
                        Instruction = s.Instruction,
                        IsAiIllustration = s.MediaAsset != null && s.MediaAsset.SourceType == MediaSourceType.AiIllustration
                    };

                    var stepKey = $"step_{recipe.Id}_{s.Id}";
                    if (mediaResults.TryGetValue(stepKey, out var stepPath) && stepPath != null)
                    {
                        line.ImageFullPath = stepPath;
                    }

                    stepLines.Add(line);
                }

                pdfModel.Steps = stepLines;
                models.Add(pdfModel);
            }

            return new RecipePdfModelBatch(models, tempDirectory);
        }
        catch (Exception)
        {
            // Ensure temp directory is deleted if factory encounters error during creation
            if (Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch
                {
                    // Ignore
                }
            }

            throw;
        }
    }
}
