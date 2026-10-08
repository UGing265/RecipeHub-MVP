---
title: "Prompt thống nhất và ảnh thành phẩm FLUX.2 Klein 4B"
description: "Dựng prompt có cấu trúc từ full context, chuẩn hóa dịch trung thành Gemini/Llama, chuyển sinh ảnh sang FLUX.2 Klein 4B với 4 preset tỷ lệ, bổ sung ảnh thành phẩm hỗ trợ Upload/Library/AI hiển thị đa bề mặt."
status: superseded
priority: P1
effort: 14h
branch: feat/pdf-layout
tags: [feature, ai, backend, ui, pdf]
blockedBy: [261006-2153-gemini-translation-round-robin]
blocks: []
created: 2026-10-06
spec: docs/superpowers/specs/2026-10-06-unified-ai-image-prompt-design.md
supersededBy: ../261008-0200-dual-ai-provider-services/plan.md
---

# Kế hoạch triển khai: Prompt thống nhất và ảnh thành phẩm công thức

Kế hoạch này hiện thực hóa toàn bộ đặc tả kỹ thuật từ:
`docs/superpowers/specs/2026-10-06-unified-ai-image-prompt-design.md`

## 1. Tóm tắt giải pháp

Hệ thống giải quyết 4 khoảng trống hiện tại:
1. **Dữ liệu context bước bị thiếu**: Thay vì chỉ gửi 1 bước lẻ, server gom toàn bộ nguyên liệu + mọi bước trước + bước hiện tại vào một cấu trúc chuẩn. Không lọc keyword, không gửi bước tương lai.
2. **Dịch bị tóm tắt / sáng tác**: Gemini và Cloudflare Llama dùng chung một system instruction trung thành (không tóm tắt, không đổi động từ, không biến đổi thể chất/màu sắc của nguyên liệu, tăng `maxOutputTokens` lên 4.096).
3. **Ảnh bị vuông cố định và model cũ**: Chuyển từ `flux-1-schnell` sang model Workers AI native `@cf/black-forest-labs/flux-2-klein-4b` (dùng quota free 10.000 Neurons/ngày), hỗ trợ 4 preset tỷ lệ (1:1, 4:3, 16:9, 4:5) qua `multipart/form-data`.
4. **Thiếu ảnh sản phẩm cuối cùng (Hero)**: Bổ sung quan hệ `Recipe.FinalMediaAssetId`, hỗ trợ 3 nguồn (Upload thật, Thư viện media, AI sinh ảnh thành phẩm), hiển thị thống nhất tại Edit, Index, Preview và PDF (QuestPDF fit theo khung preset, không cắt byte gốc).
5. **Dọn dẹp logging & an toàn**: Ẩn log SQL `Executed DbCommand` mức Information, không log full prompt/response nhạy cảm, tăng `PromptSnapshot` lên 8.000 ký tự.

## 2. Kiến trúc dữ liệu và dịch vụ

```text
Database (SQLite / EF Core)
  ├─ Recipe (FinalMediaAssetId, FinalImageAspectRatioPreset)
  ├─ RecipeStep (ImageAspectRatioPreset)
  └─ AiImageDraft (RecipeId, RecipeStepId?, TargetKind, AspectRatioPreset, PromptSnapshot: 8000)
          ↓
AiImagePromptBuilder
  ├─ BuildStepSourcePrompt(recipe, ingredients, priorSteps, currentStep, userBrief)
  ├─ BuildFinalProductSourcePrompt(recipe, ingredients, allSteps, generalNote, userBrief)
  └─ AttachCanonicalStyle(targetKind, translatedEnglishText)
          ↓
IAiPromptTranslator (GeminiRoundRobinPromptTranslator / Llama fallback)
  └─ Dùng chung AiPromptTranslationConstants.SystemInstruction
          ↓
IAiImageGenerator (CloudflareWorkersAiImageGenerator)
  └─ Model: @cf/black-forest-labs/flux-2-klein-4b (multipart: prompt, width, height, seed)
          ↓
Handler / UI / PDF
  ├─ Edit.cshtml (Ảnh từng bước + Khối ảnh thành phẩm mới)
  ├─ Recipes/Index.cshtml (Thumbnail ảnh thành phẩm)
  ├─ Recipes/Preview.cshtml (Hero ảnh thành phẩm)
  └─ RecipePdfDocument (Hero image trong tài liệu xuất bản)
```

## 3. Danh sách các Phase

| Phase | Tên giai đoạn | Mục tiêu chính | Ước lượng |
|---|---|---|---|
| **Phase 1** | [Dữ liệu, Migration và Logging](./phase-01-data-model-and-logging.md) | Schema SQLite, `Recipe.FinalMediaAssetId`, `AiImageDraft` target/preset, tăng `PromptSnapshot` 8.000, tắt log SQL | 2.5h |
| **Phase 2** | [Prompt Builder & Translation Contract](./phase-02-prompt-builder-and-translation.md) | Structured source prompt, system prompt dịch trung thành dùng chung, `maxOutputTokens=4096`, nhãn section | 3.0h |
| **Phase 3** | [FLUX.2 Klein 4B & Kích thước Preset](./phase-03-flux-klein-generator.md) | `IAiImageGenerator` nhận request DTO, Cloudflare client multipart, 4 preset kích thước, decode base64 | 2.5h |
| **Phase 4** | [Ảnh thành phẩm & Tích hợp UI / PDF](./phase-04-final-product-and-surfaces.md) | Upload / Library / AI cho ảnh thành phẩm, quản lý draft 2 target, hiển thị tại Edit/Index/Preview/PDF | 4.0h |
| **Phase 5** | [Kiểm thử tự động & Tài liệu](./phase-05-verification-and-docs.md) | Unit tests đa kịch bản, regression tests, cập nhật `MEDIA_AND_AI_OPERATIONS.md` và hướng dẫn vận hành | 2.0h |

## 4. Tiêu chí hoàn thành tổng thể (Acceptance Criteria)

1. `dotnet test` vượt qua 100% test suite, bao gồm các regression test 429 và kiểm thử mới.
2. Step image chỉ thể hiện đúng hành động của bước hiện tại, không chứa hành động bước khác hoặc bước tương lai.
3. Final product image thể hiện thành phẩm hoàn chỉnh, không có tay, người, chuỗi thao tác hoặc đạo cụ thừa.
4. Gemini và Llama dịch đủ thông tin, không tự tóm tắt hoặc sáng tác mô tả visual riêng.
5. Cả 4 preset kích thước (Vuông, Ngang chuẩn, Ngang rộng, Dọc) tạo ảnh native thành công qua FLUX.2 Klein 4B.
6. Ảnh thành phẩm có thể gán từ upload file, chọn thư viện media hoặc tạo nháp AI; hiển thị đúng ở cả 4 bề mặt: Edit, Index, Preview và PDF.
7. Log terminal không còn in SQL queries `Executed DbCommand` mức Information, không rò rỉ prompt hay provider response thô.
