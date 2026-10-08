# Phase 04 — Provider Routing, RunPod Adapter, and Draft Persistence

## Context

Plan: [`plan.md`](./plan.md)  
Requires: completed Phase 1 contract evidence and Phase 3 recipe workflow extraction.

## Current baseline (2026-10-08)

Already implemented:

- `Models/AiImageProvider.cs`
- `Services/Ai/Generation/AiImageOptions.cs`
- `Services/Ai/Generation/AiImageGeneratorRouter.cs`
- `AiImageDraft.Provider` plus EF migration
- provider selection plumbing through `RecipeImageService`
- Razor selectors with capability gating

Intentional stub:

- `AiImageGeneratorRouter` rejects RunPod because no `RunPodAiImageGenerator` exists.
- `IsProviderAvailable(RunPodFluxDev)` returns `false`.
- `RunPod` configuration is not bound or consumed.

This phase now replaces only that stub. Do not rewrite completed Cloudflare routing or persistence.

## Files

Create:

```text
src/RecipeCard.Web/Services/Ai/Providers/RunPod/RunPodOptions.cs
src/RecipeCard.Web/Services/Ai/Providers/RunPod/RunPodAiImageGenerator.cs
src/RecipeCard.Web/Services/Ai/Providers/RunPod/RunPodProtocolModels.cs
tests/RecipeCard.Web.Tests/RunPodAiImageGeneratorTests.cs
```

Modify:

```text
src/RecipeCard.Web/Services/Ai/Generation/AiImageGeneratorRouter.cs
src/RecipeCard.Web/Program.cs
src/RecipeCard.Web/appsettings.json
tests/RecipeCard.Web.Tests/AiImageGeneratorRouterTests.cs
```

Configuration:

```text
AiImage:DefaultProvider
AiImage:RunPodEnabled
RunPod:EndpointId
RunPod:ApiKey
RunPod:RequestTimeoutSeconds
RunPod:PollIntervalMilliseconds
```

`RunPod:ApiKey` comes only from user-secrets/environment in real use. `appsettings.json` contains no key value.

## Generator boundary

Keep provider implementations separate:

- `CloudflareWorkersAiImageGenerator`: existing Cloudflare contract.
- `RunPodAiImageGenerator`: verified RunPod contract only.
- `AiImageGeneratorRouter`: maps `AiImageProvider` to generator.

The router contains no HTTP, polling, prompt, persistence, or fallback logic. With two providers, use an explicit switch or keyed DI only if it remains simpler than a custom registry.

## Implementation sequence

### Task 1 — Bind and validate RunPod configuration

1. Write failing tests for missing endpoint ID, missing API key, invalid timeout, and invalid polling interval.
2. Implement `RunPodOptions` with `SectionName = "RunPod"`.
3. Validate RunPod options only when `AiImage:RunPodEnabled=true`; Cloudflare-only startup remains valid.
4. Register options and named `HttpClient` in `Program.cs`.
5. Ensure configuration exceptions contain field names but never secret values.

### Task 2 — Encode the verified request

1. Add fixture-driven serialization test for:

```json
{"input":{"prompt":"...","size":"1024x1024","seed":42}}
```

2. Map `AiImageGenerationRequest.Width`/`Height` to endpoint-supported `size` values proven in Phase 1.
3. Reject unsupported dimensions before HTTP submission; never silently resize or substitute.
4. Generate a seed deliberately: preserve explicit seed if request model gains one, otherwise use a documented random seed strategy.

### Task 3 — Implement bounded queue lifecycle

1. Submit to `/run`.
2. Parse job ID and initial status.
3. Poll `/status/{jobId}` with `PeriodicTimer` or cancellation-aware `Task.Delay`.
4. Stop at configured total timeout.
5. Handle only terminal states observed in Phase 1 fixtures.
6. Propagate caller cancellation without wrapping it as provider failure.
7. Never retry generation automatically and never call Cloudflare.

### Task 4 — Parse and validate endpoint-specific output

1. Write parser tests from the sanitized successful fixture captured in Phase 1.
2. Decode Base64 or download URL according to that fixture; support only representations actually observed.
3. Limit downloaded bytes before buffering and reject non-success HTTP responses.
4. Pass bytes through existing `IImageValidator`.
5. Return `GeneratedImage` with actual MIME type and stable model identifier `runpod/flux-1-dev`.
6. Malformed/empty output returns actionable Vietnamese error and creates no draft.

### Task 5 — Replace router stub with real capability

1. Inject both `CloudflareWorkersAiImageGenerator` and `RunPodAiImageGenerator`.
2. `IsProviderAvailable(RunPodFluxDev)` returns true only when the RunPod adapter is registered, `RunPodEnabled=true`, and required options validate.
3. `GenerateAsync(..., RunPodFluxDev)` invokes only RunPod.
4. Remove the current `"adapter chưa được triển khai"` exception.
5. Keep locked Cost Guard error when `RunPodEnabled=false`.
6. Add tests proving Cloudflare is never invoked after RunPod selection, including RunPod failure.

### Task 6 — End-to-end verification

1. Run focused RunPod serialization/parser/polling tests.
2. Run router and recipe image lifecycle tests.
3. Run `dotnet test`.
4. Launch the real app with local user-secrets.
5. Verify RunPod option becomes enabled only when adapter/config are valid.
6. Generate one square draft, observe `COMPLETED`, validate image, and verify persisted provider/model.
7. Exercise one controlled RunPod failure and verify zero Cloudflare calls and zero new drafts.

## Request selection

Extend the recipe image workflow request with nullable provider override:

```text
selected provider = explicit form override ?? configured default
```

Regenerate precedence:

```text
explicit form override ?? existing draft provider ?? configured default
```

Existing drafts created before migration receive Cloudflare Schnell through migration default/backfill.

## RunPod lifecycle

Implement according to Phase 1 evidence:

1. Submit job to `/run` or the endpoint's verified operation.
2. Parse job ID.
3. Poll status using cancellation-aware delay.
4. Stop on verified terminal success/failure states.
5. Enforce total timeout.
6. Download or decode output according to observed structure.
7. Validate bytes with existing `IImageValidator`.
8. Return `GeneratedImage` with actual MIME type and model identifier.

No automatic Cloudflare fallback. Do not retry failed generation unless the endpoint contract and product requirements explicitly define a safe transport-only retry.

## Persistence

Add `Provider` to `AiImageDraft` and create an EF Core migration:

- non-null provider value;
- existing rows map to Cloudflare Schnell;
- generated drafts store selected provider and returned model;
- acceptance does not alter provider metadata;
- regeneration creates a new draft with its selected provider.

Use enum persistence consistent with existing entity conventions. Review generated migration and SQLite snapshot; do not hand-wave schema output.

## Error behavior

Map provider failures to actionable Vietnamese messages without leaking raw response bodies or secrets. Distinguish at minimum:

- configuration/authentication;
- queue/execution timeout;
- provider terminal failure;
- malformed output;
- failed image validation;
- caller cancellation.

## Tests

- Router selects each generator and rejects unknown provider.
- Explicit provider overrides configured default.
- Missing override uses configured default.
- Regenerate retains draft provider by default.
- Explicit regenerate override changes provider for the new draft.
- RunPod queue/progress/completion parsing matches captured fixtures.
- Failure, timeout, cancellation, malformed output, and URL/Base64 handling match the verified contract.
- API key and raw sensitive response are absent from errors/log assertions.
- Migration default/backfill preserves existing drafts.
- Cloudflare tests remain unchanged in meaning.

## Completion gate

- [ ] Real contract fixtures drive RunPod parser tests.
- [ ] Both provider implementations resolve independently.
- [ ] Default/override precedence is deterministic.
- [ ] No implicit fallback exists.
- [ ] Draft provider migration and snapshot are correct.
- [ ] Invalid RunPod output creates no draft.
