# Phase 3 — Media library and recipe integration

## Context

- Parent: [Cloudinary media library and AI image-candidate demo](./plan.md)
- Depends on: [Asset, draft data, and legacy cutover](./phase-02-asset-data-and-legacy-cutover.md)
- UI system: `docs/materials/DESIGN.md` and `AGENTS.md` require Mobbin monochrome, pill interactions, 24px containers, 16px media, hairline borders, no shadows; blue is not a general action color.

## Overview

Ship a server-rendered media library and wire real-photo assets into recipe creation, preview, and PDF export. AI candidates arrive only in Phase 4; library presentation already labels `Ảnh chụp` versus `AI minh họa`.

**Priority:** P2  
Status: Completed  
**Estimate:** 5h

## Requirements

- Add sidebar route `/Media/Index` labelled `Thư viện ảnh`.
- Gallery shows active assets: safe delivery URL, original name, source label, size, creation time, reference count.
- Filters: `Tất cả`, `Ảnh chụp`, `Minh họa AI`, `Lỗi xóa`; AI can be empty until Phase 4.
- Upload creates library assets even before attaching them to a step.
- New-step form permits exactly one source: upload, existing active asset, or no image. Server rejects multiple source inputs.
- Only media-library deletion can destroy remote storage. Referenced assets cannot be deleted; failure stays retryable.
- Preview reads `DeliveryUrl`; PDF downloads bounded bytes through server `HttpClient`, never derives a local path from a filename.

## Related code files

| Action | Path | Change |
|---|---|---|
| Create | `src/RecipeCard.Web/Pages/Media/Index.cshtml`, `Index.cshtml.cs` | Paged gallery, filters, upload, reference-safe delete/retry. |
| Modify | `src/RecipeCard.Web/Pages/Shared/_Layout.cshtml` | Media sidebar route/active state. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml`, `Edit.cshtml.cs` | Upload/select media for a new step; render existing selected asset. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml`, `Preview.cshtml.cs` | Render delivery URL and source label. |
| Modify | `src/RecipeCard.Web/Pdf/RecipePdfDocument.cs` | Receive byte payload and conditionally print AI-illustration label. |
| Modify/Create | `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs`, `MediaLibraryTests.cs` | Gallery, reference deletion, asset attach, URL/PDF contracts. |

## Implementation steps

1. Create paged `/Media/Index` query with projection/reference count, source/state filters, newest-first ordering; never scan a directory.
2. Build gallery from current Mobbin classes: 16px media tile, hairline border, pill labels, no box shadows/Bootstrap semantic-color actions.
3. Implement upload with Phase 1 storage/Phase 2 metadata persistence and compensation on DB failure.
4. Implement deletion/retry: count step references first; reject referenced assets; mark `DeleteFailed` if provider destroy fails; delete DB record only after success.
5. Add sidebar item under management catalog.
6. Extend add-step handler with `mediaAssetId` and enforce `image XOR mediaAssetId`; link valid active asset without a second provider upload.
7. Eager-load asset navigation for edit/preview to avoid N+1 reads.
8. Fetch PDF bytes with `ResponseHeadersRead`, image MIME/size checks, timeout/cancellation; error without mutating recipe data.
9. Render source label in editor, preview, PDF. The `AI minh họa` path remains empty until Phase 4.
10. Test then smoke-test: upload → gallery → attach same asset to two steps → preview/PDF → attempted referenced delete.

## Success criteria

- One uploaded image can be reused by two steps without duplicate upload.
- Referenced assets are visibly protected from deletion.
- Preview/PDF use Cloudinary HTTPS delivery rather than `/uploads/steps/https://...`.
- PDF remote failure gives an explicit error, never corrupts recipe state.
- UI conforms to project design system at desktop/tablet layouts.

## Risks and controls

| Risk | Control |
|---|---|
| Cloud delivery unavailable during PDF | finite timeout and explicit error. |
| Unbounded gallery | pagination/projection; no filesystem scan. |
| Stale selected asset ID | re-query ID/state in POST handler. |
| AI mistaken for photo | source enum + same label surface prepared before generation exists. |
| Reusable asset deleted by one step | detaching a step never deletes media; deletion checks references. |

## Todo

- [x] Create paged media library and sidebar route.
- [x] Implement safe upload, reference-aware delete, retry state.
- [x] Link existing asset or upload one asset when adding a step.
- [x] Convert preview/PDF to remote byte loading and source labels.
- [x] Verify gallery-to-PDF flow and visual constraints.
