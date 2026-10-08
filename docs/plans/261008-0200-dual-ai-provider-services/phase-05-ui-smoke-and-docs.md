# Phase 05 — Provider UI, Smoke Verification, and Documentation

## Context

Plan: [`plan.md`](./plan.md)

Expose the completed provider capability through the existing Razor forms without redesigning the page.

## UI behavior

Add a model/provider select to:

- final-product AI generation form;
- step AI generation form;
- final-product regeneration form;
- step regeneration form.

Labels:

```text
Cloudflare — FLUX.1 Schnell
RunPod — FLUX.1 Dev
```

Behavior:

- New generation defaults to configured provider.
- Regeneration defaults to the source draft provider.
- User may override before submission.
- Invalid enum values are rejected server-side.
- Draft display shows provider/model used.
- No Bootstrap default color classes, new shadows, decorative emoji, or accent misuse.

If Phase 1 proves provider-specific dimension constraints, show concise provider-aware help text. Do not promise native ratios unsupported by the selected endpoint.

## Real smoke verification

### Cloudflare

1. Launch the actual application.
2. Open a recipe edit page.
3. Select Cloudflare Schnell.
4. Generate one final or step draft.
5. Observe successful draft preview and persisted provider/model.
6. Accept or cancel and observe the expected state transition.

### RunPod

1. Configure endpoint ID and API key through server-side secrets.
2. Select RunPod Dev.
3. Generate one draft using the verified payload path.
4. Observe queue/progress completion within configured timeout.
5. Verify actual output MIME and dimensions.
6. Observe persisted provider/model.
7. Exercise one controlled provider failure or invalid configuration and confirm no Cloudflare call and no draft.

Use browser visual proof for the provider selector and result state. Close the browser session after verification.

## Automated verification

Run in this order after implementation:

1. focused provider/router tests;
2. focused recipe image lifecycle tests;
3. EF migration/model tests if present;
4. project build;
5. complete test suite.

Report only commands actually executed and observed results.

## Documentation

Update:

- `src/RecipeCard.Web/Services/README.md` — actual tree, lookup table, boundaries, add-provider checklist;
- `docs/materials/MEDIA_AND_AI_OPERATIONS.md` — provider configuration, selection behavior, verified RunPod contract, troubleshooting, cost/cold-start caveats;
- sample configuration files — placeholders and non-secret defaults only;
- changelog/release documentation according to repository convention.

README lookup table:

| Need to find | Open |
|---|---|
| Prompt construction | `Services/Ai/Prompting/` |
| Cloudflare API integration | `Services/Ai/Providers/Cloudflare/` |
| RunPod API integration | `Services/Ai/Providers/RunPod/` |
| Provider selection | `Services/Ai/Generation/AiImageGeneratorRouter.cs` |
| Draft storage and cleanup | `Services/Ai/Drafts/` |
| Image validation and storage | `Services/Media/` |
| Recipe image workflow | `Services/Recipes/RecipeImageService.cs` |
| Recipe PDF services | `Services/Pdf/` |

## Final acceptance

- [ ] Every listed path exists and README is accurate.
- [ ] Provider selector appears on all generation/regeneration forms.
- [ ] Default and override behavior observed in the real UI.
- [ ] Cloudflare smoke succeeds.
- [ ] RunPod smoke succeeds using user-owned endpoint credentials.
- [ ] RunPod failure does not fallback or create a draft.
- [ ] Draft provider/model visible and persisted.
- [ ] Build and full tests pass.
- [ ] Operations documentation contains no secrets or unsupported claims.
