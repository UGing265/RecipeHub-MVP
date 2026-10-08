# Phase 01 — Verify RunPod Endpoint Contract

## Context

Plan: [`plan.md`](./plan.md)  
Design: [`../261008-0200-dual-ai-provider-services-design.md`](../261008-0200-dual-ai-provider-services-design.md)

Endpoint ID: `rfxqcmcf0se7lb`.

Endpoint-specific request evidence supplied on 2026-10-08:

```json
{
  "input": {
    "prompt": "a small robot reading beside a window",
    "size": "1024x1024",
    "seed": 42
  }
}
```

Known request fields: `input.prompt`, `input.size`, `input.seed`. Endpoint-specific completed `output` remains unknown. Generic RunPod examples MUST NOT define the image parser.

## External prerequisite

- User-owned RunPod API key stored locally through `dotnet user-secrets` or environment variable. Never request or print its value.
- Endpoint running and funded.
- Never paste the key into chat, source, plan files, command output, logs, or browser JavaScript.
- Configure locally:

```powershell
dotnet user-secrets set "RunPod:EndpointId" "rfxqcmcf0se7lb" --project src/RecipeCard.Web
dotnet user-secrets set "RunPod:ApiKey" "<local-secret>" --project src/RecipeCard.Web
```

Missing credentials block only the bounded live probe and adapter parser. Cloudflare remains usable.

## Contract probe procedure

1. Add a temporary local-only probe under `scripts/` that reads `RunPod:EndpointId` and `RunPod:ApiKey` through .NET configuration. The script MUST redact authorization headers and MUST NOT serialize configuration.
2. Submit the verified request above to `POST https://api.runpod.ai/v2/{endpointId}/run`.
3. Parse only the generic RunPod queue envelope: job `id` and `status`.
4. Poll `GET https://api.runpod.ai/v2/{endpointId}/status/{jobId}` using cancellation-aware delay, 1.5-second interval, and 120-second total timeout.
5. Write a sanitized response fixture containing field names, value types, terminal status, and redacted output placeholders. Do not retain signed URLs, Base64 bytes, prompts, or identifiers.
6. Determine endpoint-specific completed output representation: direct URL, Base64, object, or list.
7. Download/decode one square result; verify MIME type and dimensions using existing `IImageValidator`.
8. Probe one supported non-square `size` value only after square success. Record whether requested dimensions are honored.
9. Capture sanitized authentication, invalid-payload, terminal-failure, and timeout behavior.
10. Delete the temporary probe after fixtures and operations documentation are complete.

RunPod generic queue operations reference: <https://docs.runpod.io/serverless/endpoints/operation-reference>. This reference establishes `/run` and `/status/{jobId}`, not the endpoint-specific image `output` shape.

## Deliverable

Add a sanitized contract section to `docs/materials/MEDIA_AND_AI_OPERATIONS.md` containing:

- endpoint type/template and version;
- request fields and constraints;
- submit response fields;
- status states and terminal states;
- completed output structure;
- error structure;
- supported dimensions demonstrated by actual output;
- timeout/cancellation assumptions.

Do not include API keys, signed URLs, generated image bytes, or full sensitive prompts.

## Verification scenarios

| Scenario | Expected evidence |
|---|---|
| Minimal generation | Terminal job reaches `COMPLETED`; image can be decoded/downloaded |
| Invalid auth | Sanitized 401/403 shape; no key in logs |
| Invalid payload | Provider validation error identifies required fields |
| Non-square dimensions | Returned image dimensions match request, or capability marked unsupported |
| Cancellation/timeout observation | Endpoint behavior documented; no infinite polling assumption |

## Completion gate

- [x] Endpoint-specific request payload recorded in sanitized form.
- [ ] Successful completed response recorded in sanitized form.
- [ ] Output representation and MIME type known.
- [ ] Dimension support proven, not inferred.
- [ ] Failure states identified.
- [ ] No secret persisted or printed.
