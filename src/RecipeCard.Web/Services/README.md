# Services Directory Structure

Directory contains core business and infrastructure services for RecipeCard.Web.
All services currently share root namespace `RecipeCard.Web.Services`.

## Domain Map

| Domain / Subdirectory | Purpose | Key Files |
|---|---|---|
| `Ai/Generation/` | Core contracts and request models for AI image generation | `IAiImageGenerator.cs`, `AiImageGenerationRequest.cs` |
| `Ai/Providers/Cloudflare/` | Cloudflare Workers AI image generation adapter and options | `CloudflareWorkersAiImageGenerator.cs`, `CloudflareOptions.cs` |
| `Ai/Providers/Gemini/` | Google Gemini prompt translation with key cursor round-robin | `GeminiRoundRobinPromptTranslator.cs`, `GeminiKeyCursor.cs`, `GeminiOptions.cs` |
| `Ai/Prompting/` | Recipe prompt builders, constants, fallback translation adapters | `AiImagePromptBuilder.cs`, `AiPromptTranslationConstants.cs`, `IAiPromptTranslator.cs`, `CloudflareWorkersAiPromptTranslator.cs` |
| `Ai/Drafts/` | Temporary draft image filesystem storage and background cleanup | `AiDraftFileStore.cs`, `AiDraftCleanupHostedService.cs` |
| `Media/Storage/` | Permanent image storage abstraction, local disk and Cloudinary | `IImageStorageService.cs`, `ImageStorageService.cs`, `CloudinaryImageStorageService.cs`, `CloudinaryOptions.cs` |
| `Media/Validation/` | Image validation: file size, dimensions, magic bytes (.jpg, .png, .webp) | `ImageValidator.cs` |
| `Media/Migration/` | Step image migration from local disk to Cloudinary | `LegacyStepImageMigrationService.cs` |
| `Media/` | Shared media extensions | `AspectRatioPresetExtensions.cs` |
| `Recipes/` | Recipe image lifecycle management (final and step draft creation, regeneration, accept, discard) | `RecipeImageService.cs` |
| `Pdf/` | QuestPDF model building and high-concurrency image preloading | `RecipePdfModelFactory.cs`, `PdfMediaLoader.cs` |

## Planned Components

- `Ai/Providers/RunPod/` — RunPod FLUX.1 Dev endpoint adapter (`RunPodAiImageGenerator.cs`, `RunPodOptions.cs`, `RunPodProtocolModels.cs`).
- `Ai/Generation/AiImageGeneratorRouter.cs` — Explicit provider router supporting Cloudflare Schnell vs RunPod Dev.

## Dependency Flow

```text
Pages (Edit.cshtml.cs)
  └─► RecipeImageService (Recipes/)
        ├─► AiImagePromptBuilder & IAiPromptTranslator (Ai/Prompting/, Ai/Providers/Gemini/)
        ├─► IAiImageGenerator (Ai/Generation/, Ai/Providers/Cloudflare/)
        ├─► AiDraftFileStore (Ai/Drafts/)
        └─► IImageStorageService & IImageValidator (Media/Storage/, Media/Validation/)
```

## Ownership Boundaries

1. **Providers do not persist drafts**: Image generators return raw bytes; drafts are saved only via `AiDraftFileStore`.
2. **Storage does not choose providers**: Image storage only saves accepted media; provider routing happens before generation.
3. **Prompt services do not generate images**: Prompt translation and structured prompt construction only format text.
4. **No cross-contamination**: Providers never trigger silent fallback to another provider.

## Adding a Provider Checklist

1. Create dedicated folder: `Services/Ai/Providers/{ProviderName}/`.
2. Add strongly-typed options class validating credentials only when that provider is active.
3. Implement `IAiImageGenerator` without handling draft persistence or fallback.
4. Register options and generator in DI (`Program.cs`).
5. Update router `switch` statement in `AiImageGeneratorRouter`.
6. Add unit tests for request/response serialization and error states using observed contract fixtures.
7. Update `docs/materials/MEDIA_AND_AI_OPERATIONS.md` with operational guidance and secret requirements.
