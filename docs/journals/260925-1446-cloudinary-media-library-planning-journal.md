---
title: "Cloudinary media library planning journal"
date: 2026-09-25
plan: "plans/260925-1446-cloudinary-media-library/plan.md"
---

# Cloudinary media library planning journal

## Context
The plan now covers Cloudinary-backed reusable media assets plus one accepted-only AI illustration demo for saved recipe steps.

## Decisions
- Cloudinary remains sole durable media storage after cutover. Local `App_Data/ai-drafts` holds temporary, unaccepted candidate bytes only; it is not a fallback media store.
- `MediaAsset` owns Cloudinary public ID, delivery URL, original-file metadata, provenance, lifecycle state, timestamps, and an optional accepted-draft link.
- `AiImageDraft` records prompt snapshot, bounded user brief, `@cf/black-forest-labs/flux-1-schnell` (Cloudflare Workers AI free tier, 10,000 Neurons/day), state, expiry, and a generated temporary filename.
- Backend simply combines recipe name, step description, and the user's optional visual brief into a clean prompt for FLUX.1. Complex prompt-injection sanitization and rigid overriding guardrails are excluded to keep code simple and give trusted R&D staff full creative freedom.
- Candidate generation, regeneration, and discard never upload to Cloudinary. Only explicit acceptance uploads exact staged bytes, creates `SourceType = AiIllustration`, and links the saved step.
- Existing local files migrate through an explicit local `--confirm` command. Missing/invalid files remain intact for repair; no silent loss.

## Key implementation risks
- Keep Cloudinary/Cloudflare credentials, authorization, canonical prompts, and provider internals server-only.
- Validate model-returned bytes before writing a draft or uploading Cloudinary.
- Guard candidate state/expiry during acceptance; compensate Cloudinary upload if database persistence fails.
- Stream drafts only through a controlled handler from outside `wwwroot`; TTL cleanup prevents disk accumulation.
- PDF fetches accepted Cloudinary URLs with timeout; generation candidates never reach preview/PDF.

## AI boundary
Single demo only: Cloudflare Workers AI running `@cf/black-forest-labs/flux-1-schnell` (free-tier edge diffusion), one candidate per saved step, optional brief, regenerate/discard/accept. No proprietary SDK needed (standard .NET `HttpClient` calls Cloudflare REST API).

## Next
Review the candidate UX and plan; implementation remains intentionally unstarted.

## Status
Planning documentation only; no implementation was performed.
