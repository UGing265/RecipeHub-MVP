# Phase 2 — Asset, draft data, and legacy cutover

## Context

- Parent: [Cloudinary media library and AI image-candidate demo](./plan.md)
- Depends on: [External image foundations](./phase-01-cloudinary-storage-foundation.md)
- Existing schema: `RecipeStep.ImageFileName` is an optional local filename; no media provenance, reusable asset record, or AI candidate lifecycle exists.

## Overview

Add reusable `MediaAsset` records, temporary `AiImageDraft` audit/lifecycle records, and a staged conversion path for current local step files. Candidate image files remain below `App_Data/ai-drafts`, never static or public.

**Priority:** P1  
Status: Completed  
**Estimate:** 5h

## Requirements

- One asset may serve many steps; one current step may have zero or one selected asset.
- `MediaAsset` stores Cloudinary public ID/delivery URL and has `SourceType = Real | AiIllustration`.
- Each step has no more than one active AI candidate. Every draft links to an already saved step, has expiry, prompt snapshot, user brief, fixed model, temporary generated filename, and state.
- Accepted draft links exactly one AI `MediaAsset`; rejected/expired files never become media assets.
- Existing local files convert through a local command requiring `--confirm`; missing/corrupt sources are reported and remain untouched.

## Related code files

| Action | Path | Change |
|---|---|---|
| Create | `src/RecipeCard.Web/Models/MediaAsset.cs`, `AiImageDraft.cs` | Asset/draft entities and source/state enums. |
| Modify | `src/RecipeCard.Web/Models/RecipeStep.cs` | Add nullable asset FK/navigation; retain `ImageFileName` only through migration. |
| Modify | `src/RecipeCard.Web/Data/RecipeDbContext.cs` | DbSets, limits, indexes, asset/draft FKs, filtered unique active-draft rule if SQLite mapping supports it. |
| Create | `src/RecipeCard.Web/Migrations/*AddMediaAssetAndAiDraft*.cs` | Add tables and nullable step asset FK. |
| Create | `src/RecipeCard.Web/Services/AiDraftFileStore.cs` | Write/open/delete generated files under a fixed content-root directory. |
| Create | `src/RecipeCard.Web/Services/LegacyStepImageMigrationService.cs` | Local command implementation and conversion report. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs` | Persist direct upload metadata with compensation. |
| Create/Modify | `tests/RecipeCard.Web.Tests/LegacyMediaMigrationTests.cs`, `AiImageDraftTests.cs` | Data lifecycle, path safety, conversion contracts. |

## Implementation steps

1. Add fields/constraints from parent plan. Generated storage filename is never caller controlled; all times UTC.
2. Configure EF Core:
   - unique `MediaAsset.ProviderPublicId`;
   - required/limited delivery URL and metadata;
   - nullable indexed `RecipeStep.MediaAssetId`, `SetNull`;
   - `AiImageDraft.RecipeStepId` required and indexed;
   - accepted draft can link one asset; candidate/draft states use an enum, not free text.
3. Generate/apply additive migration against a copy of `recipe-card.db`; existing recipe graph must still load through `ImageFileName`.
4. Implement `AiDraftFileStore` using `ContentRootPath/App_Data/ai-drafts`; require generated filename/ID mapping, create directory safely, validate returned bytes, and delete only under its root.
5. Change direct image upload flow: validate/upload → DB transaction creating `MediaAsset` and step link → best-effort remote delete if DB persistence fails.
6. Implement the local conversion command, e.g. `dotnet run -- --migrate-step-images --confirm`:
   - enumerate `ImageFileName != null && MediaAssetId == null`;
   - resolve inside known legacy folder; reject traversal, revalidate bytes;
   - upload, insert asset, link step, commit per file;
   - preserve local source until persistence succeeds;
   - output converted, missing, invalid, provider-failed, database-failed counts and names.
7. Only after a clean reviewed report, create a later cleanup migration removing `ImageFileName`, local renderer, and unused local persistence path.

## Success criteria

- A direct valid upload yields exactly one Cloudinary asset, one `MediaAsset`, and the selected step link.
- Temporary candidates cannot be browser-served through `wwwroot` or a caller-supplied path.
- Existing local files have auditable migration outcomes; unresolved rows retain the old reference.
- Database constraints prevent two current generated candidates for one step.

## Risks and controls

| Risk | Control |
|---|---|
| Remote/SQLite cannot share transaction | compensation for upload; candidate accept keeps staged file for retry. |
| Candidate path traversal/public exposure | fixed `App_Data` root, generated names, controlled stream endpoint only. |
| Partial legacy conversion | per-item commits and report; rerun unresolved items only. |
| Data loss from field removal | separate, post-report cleanup migration. |

## Todo

- [x] Add asset/draft schema and staged migration.
- [x] Implement bounded temporary candidate file store.
- [x] Preserve direct-upload compensation and local conversion command.
- [x] Test draft constraints, paths, and migration outcomes.
