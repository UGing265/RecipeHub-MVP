# Phase 02 — Organize Services by Domain

## Context

Plan: [`plan.md`](./plan.md)

This phase is behavior-neutral. Move existing files only, add the directory README, preserve public contracts, and avoid provider or recipe lifecycle changes.

## Target mapping

| Current file | Target directory |
|---|---|
| `IAiImageGenerator.cs` | `Services/Ai/Generation/` |
| `AiImageGenerationRequest.cs` | `Services/Ai/Generation/` |
| `CloudflareWorkersAiImageGenerator.cs` | `Services/Ai/Providers/Cloudflare/` |
| `CloudflareOptions.cs` | `Services/Ai/Providers/Cloudflare/` |
| `GeminiRoundRobinPromptTranslator.cs` | `Services/Ai/Providers/Gemini/` |
| `GeminiKeyCursor.cs` | `Services/Ai/Providers/Gemini/` |
| `GeminiOptions.cs` | `Services/Ai/Providers/Gemini/` |
| `IAiPromptTranslator.cs` | `Services/Ai/Prompting/` |
| `AiImagePromptBuilder.cs` | `Services/Ai/Prompting/` |
| `AiPromptTranslationConstants.cs` | `Services/Ai/Prompting/` |
| `CloudflareWorkersAiPromptTranslator.cs` | `Services/Ai/Prompting/` |
| `AiDraftFileStore.cs` | `Services/Ai/Drafts/` |
| `AiDraftCleanupHostedService.cs` | `Services/Ai/Drafts/` |
| `IImageStorageService.cs` | `Services/Media/Storage/` |
| `ImageStorageService.cs` | `Services/Media/Storage/` |
| `CloudinaryImageStorageService.cs` | `Services/Media/Storage/` |
| `CloudinaryOptions.cs` | `Services/Media/Storage/` |
| `ImageValidator.cs` | `Services/Media/Validation/` |
| `LegacyStepImageMigrationService.cs` | `Services/Media/Migration/` |
| `AspectRatioPresetExtensions.cs` | `Services/Media/` |
| `RecipePdfModelFactory.cs` | `Services/Pdf/` |
| `PdfMediaLoader.cs` | `Services/Pdf/` |

Do not create `Services/Recipes/` until Phase 3 introduces a real recipe service.

## Namespace policy

Preserve `namespace RecipeCard.Web.Services;` during movement. Physical organization alone delivers the requested discoverability while avoiding broad `using` churn. Namespace subdivision, if later justified, requires a separate explicit refactor.

## `Services/README.md`

Include:

1. Purpose of the Services tree.
2. Lookup table for prompts, providers, routing, drafts, media, recipe image workflow, and PDF.
3. Dependency direction: PageModel → recipe workflow → provider router → provider adapter.
4. Ownership boundaries: providers do not persist drafts; storage does not choose providers; prompt services do not call image providers.
5. Rule for adding a provider: separate folder, options, generator, router entry, tests, operations documentation.
6. Mark RunPod and `RecipeImageService` as planned until their phases land; do not describe nonexistent files as current.

## Implementation steps

1. Move files using repository-aware file operations.
2. Preserve file contents and namespaces unless path-sensitive code requires a targeted correction.
3. Update project references only if explicit includes exist.
4. Add README reflecting the actual tree at phase completion.
5. Run focused compile and existing test suite once after all moves.

## Verification

- [ ] Application builds without changing behavior.
- [ ] Existing tests pass unchanged except unavoidable path/namespace references.
- [ ] DI registrations resolve exactly as before.
- [ ] No duplicate or compatibility files remain at `Services/` root.
- [ ] README paths all exist or are clearly labelled planned.
- [ ] Git recognizes moves rather than delete/recreate noise where possible.
