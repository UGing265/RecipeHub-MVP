# Phase 4 — Cloudflare Workers AI candidate approval demo

## Context

- Parent: [Cloudinary media library and Cloudflare Workers AI image-candidate demo](./plan.md)
- Depends on: [External image foundations](./phase-01-cloudinary-storage-foundation.md), [Asset, draft data, and legacy cutover](./phase-02-asset-data-and-legacy-cutover.md), [Media library and recipe integration](./phase-03-media-library-and-recipe-integration.md)
- Provider/model: Cloudflare Workers AI running `@cf/black-forest-labs/flux-1-schnell`. Free allocation of 10,000 Neurons/day (~150-200 images/day) at zero cost. BFL FLUX.1 12B rectified flow transformer model optimized for sub-second, 4-step diffusion.

## Overview

Add an inline, server-rendered candidate workflow to existing saved recipe steps. Backend owns the canonical prompt and Cloudflare REST API call. User may add a short visual brief, inspect the resulting candidate, regenerate/discard it, then explicitly accept it. Only acceptance uploads the exact staged bytes to Cloudinary and links a labelled AI illustration to the step.

**Priority:** P2  
Status: Completed  
**Estimate:** 4h

## UX contract

```text
Existing saved step
  → [Tạo minh họa AI]
  → optional “Yêu cầu thêm cho ảnh” (max 500 characters)
  → [Tạo ảnh nháp]
  → server prompt builder + Cloudflare Workers AI (FLUX.1 Schnell)
  → candidate preview
      ├─ [Tạo lại]        → replace temporary candidate
      ├─ [Bỏ ảnh]         → delete temporary candidate
      └─ [Chấp nhận ảnh này]
           → Cloudinary upload
           → MediaAsset(AiIllustration)
           → link existing RecipeStep
           → editor / preview / PDF label “AI minh họa”
```

### Prompt boundary

The browser sends only `userBrief`. It never sends a raw model/system prompt, Cloudflare Account ID, or API Token.

`AiImagePromptBuilder` constructs a straightforward prompt for FLUX.1 without complex prompt-injection sanitization (as this is an internal R&D authoring tool used by trusted staff):

1. Context: Recipe name and step instruction (e.g. "Bước 2: Rót 30ml sữa đặc vào shaker").
2. User brief: Optional stylistic note entered by user (e.g. "ly thủy tinh, góc nhìn từ trên xuống, nền gỗ sáng").
3. Style guideline: "Clear beverage preparation photo, high resolution, realistic lighting, neutral clean background."

Regeneration simply reruns the builder with the updated brief and generates a new candidate.

## Related code files

| Action | Path | Change |
|---|---|---|
| Create | `src/RecipeCard.Web/Services/AiImagePromptBuilder.cs` | Deterministic prompt template and bounded brief validation. |
| Create/Modify | `src/RecipeCard.Web/Services/IAiImageGenerator.cs`, `CloudflareWorkersAiImageGenerator.cs`, `AiDraftFileStore.cs` | Generate bytes via Cloudflare REST API, stage/open/delete temporary candidates. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml` | Inline AI panel, candidate preview, action forms, provenance label. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs` | Generate/regenerate/discard/accept/preview handlers; state and expiry checks. |
| Modify | `src/RecipeCard.Web/Data/RecipeDbContext.cs` | Candidate query helpers/indexes if not completed in Phase 2. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml`, `Pdf/RecipePdfDocument.cs` | Render permanent `AI minh họa` label for accepted asset only. |
| Create/Modify | `tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs`, `RecipeComposerTests.cs` | Candidate lifecycle, no-premature-upload, acceptance/error tests. |

## Implementation steps

1. Implement prompt builder as a pure service. Validate step exists/is saved, trim and limit brief, wrap it as untrusted optional direction, and produce a deterministic prompt snapshot for the draft record.
2. Add `GenerateAiImage` handler for an existing recipe step:
   - load step/recipe data;
   - reject if a current non-expired candidate exists unless request is explicit regenerate;
   - generate with `IAiImageGenerator` using Cloudflare Workers AI (`@cf/black-forest-labs/flux-1-schnell`);
   - validate returned bytes, save below `App_Data/ai-drafts`, insert `AiImageDraft(Generated)` with bounded TTL;
   - redirect back to edit page candidate panel; do not call `IImageStorageService`.
3. Add controlled candidate preview handler:
   - fetch draft by GUID plus current recipe/step relation;
   - allow only `Generated` and non-expired record;
   - stream validated bytes with stored MIME; no static URL and no directory/file-name input.
4. Add `RegenerateAiImage` handler:
   - delete/supersede prior active temporary file/record;
   - call the same generate path with a revised brief;
   - retain failed provider message as safe TempData only, never write a broken draft.
5. Add `DiscardAiImage` handler: mark draft `Discarded`, delete its staged file, and return to unchanged step.
6. Add `AcceptAiImage` handler:
   - reload draft, require `Generated`, current, linked to requested step;
   - open staged bytes and validate again;
   - upload exact bytes to Cloudinary;
   - in one database transaction create `MediaAsset` with `SourceType.AiIllustration`, link `AiImageDraftId`, replace/link the step asset, mark draft `Accepted`;
   - on DB failure, best-effort Cloudinary destroy; on Cloudinary failure, keep draft `Generated` so the user can retry acceptance;
   - delete temporary file only after successful commit.
7. Add expiry cleanup as a bounded hosted maintenance service: periodically mark stale generated drafts `Expired` and delete only their files inside draft root. No background generation queue.
8. Update editor/preview/PDF markup to label accepted generated assets `AI minh họa`; never render draft images in preview/PDF/media library before acceptance.
9. Build fake-provider tests and one authentication-free smoke sequence with fake provider: create candidate → regenerate → discard → create → accept → Media library → preview/PDF.

## Success criteria

- User can adjust a short visual brief and create/recreate a candidate without typing or seeing a raw system prompt.
- Generated/rejected/expired candidate bytes never upload to Cloudinary and never appear in the media gallery or PDF.
- Accepting a current candidate uploads exactly once, creates one `AiIllustration` asset with model/prompt provenance, and attaches it to its existing step.
- Cloudflare failure, expired candidate, mismatched step/draft ID, Cloudinary failure, or DB failure leave existing recipe/asset data unchanged.
- Every accepted AI asset is visibly distinct from a real uploaded photo in editor, preview, gallery, and PDF.

## Security and failure controls

| Risk | Control |
|---|---|
| API token or canonical prompt exposed | server-only service/options; browser sees safe summary/result only. |
| Unintended prompt visual output | User reviews candidate draft with freedom to regenerate or discard before accept. |
| Draft guessed or publicly indexed | GUID plus step relation; stream handler; files outside `wwwroot`. |
| Cost from repeated clicks | disable UI while POST is pending; one active candidate per step; manual regenerate only. 10,000 free neurons reset daily. |
| Acceptance races/regenerate races | re-query draft state in transaction; reject non-current state. |
| Disk accumulation | TTL + bounded cleanup; reject/accept deletes staged bytes. |

## Todo

- [x] Implement deterministic server prompt builder and Cloudflare FLUX.1 adapter.
- [x] Add temporary candidate lifecycle and controlled preview.
- [x] Add regenerate/discard/accept handlers with accepted-only Cloudinary upload.
- [x] Label accepted AI assets in every rendering surface.
- [x] Test candidate-state and provider-failure contracts.
