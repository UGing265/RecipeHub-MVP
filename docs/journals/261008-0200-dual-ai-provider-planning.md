---
title: Dual AI Provider Planning
created: 2026-10-08
---

# Dual AI Provider Planning

## Context

The application already generates recipe images with Cloudflare FLUX.1 Schnell. A RunPod FLUX.1 Dev endpoint now exists, but its real request/output contract has not yet been exercised. The flat `Services/` directory also makes ownership difficult to discover.

## Decisions

- Keep Cloudflare Schnell and add RunPod Dev; do not replace the existing provider.
- Use a configured default plus per-generate/per-regenerate override.
- Use a small explicit provider router with separate adapters.
- Never fallback automatically from RunPod to Cloudflare.
- Persist provider and actual model on each AI draft.
- Verify the live RunPod contract before writing its parser.
- Reorganize `Services/` by domain and add a nearby `Services/README.md`.
- Move files without behavior changes before extracting recipe image workflow from the PageModel.
- Keep unrelated recipe CRUD outside this refactor.

## Plan maintenance

The old FLUX.2 Klein plan conflicted with the new decision to retain Schnell and add Dev. It was marked `superseded` and linked to the current plan.

Current implementation plan:

`docs/plans/261008-0200-dual-ai-provider-services/plan.md`

## Next

Execute Phase 1 RunPod contract verification and Phase 2 behavior-neutral service organization. RunPod verification requires the user's endpoint to be active and its API key to be available through a local secret; the key must not enter source or documentation.
