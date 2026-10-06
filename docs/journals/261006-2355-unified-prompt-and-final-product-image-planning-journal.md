# Nhật ký lập kế hoạch: Prompt thống nhất và ảnh thành phẩm công thức FLUX.2 Klein 4B

- **Thời gian**: 2026-10-06
- **Kế hoạch**: `docs/plans/261006-2350-unified-prompt-and-final-product-image/plan.md`
- **Đặc tả gốc**: `docs/superpowers/specs/2026-10-06-unified-ai-image-prompt-design.md`

## 1. Kết quả phân tích & Thiết kế
1. **Dữ liệu & Migration (Phase 1)**:
   - Thêm `FinalMediaAssetId`, `FinalImageAspectRatioPreset` vào `Recipe`.
   - Thêm `ImageAspectRatioPreset` vào `RecipeStep`.
   - Thêm `RecipeId`, `TargetKind`, `AspectRatioPreset` vào `AiImageDraft`, tăng cột `PromptSnapshot` lên 8.000 ký tự.
   - Cấu hình tắt log SQL `Executed DbCommand` mức Information.
2. **Prompt Builder & Dịch thuật (Phase 2)**:
   - Gom toàn bộ nguyên liệu + các bước trước vào source prompt; không lọc keyword, không đưa bước tương lai.
   - Dùng chung `AiPromptTranslationConstants.FaithfulSystemInstruction` cho cả Gemini và Llama fallback.
   - Nâng `maxOutputTokens` lên 4.096, kiểm tra nhãn section sau khi dịch.
   - Tách 2 canonical prompt: ảnh bước và ảnh thành phẩm hero.
3. **FLUX.2 Klein 4B (Phase 3)**:
   - Model Workers AI native `@cf/black-forest-labs/flux-2-klein-4b` sử dụng `multipart/form-data`.
   - 4 preset tỷ lệ: 1:1 (1024×1024), 4:3 (1024×768), 16:9 (1280×720), 4:5 (768×960).
4. **Vòng đời ảnh thành phẩm (Phase 4)**:
   - Hỗ trợ 3 nguồn: Upload file, chọn Thư viện media, tạo bản nháp AI.
   - Hiển thị đồng bộ ở 4 nơi: Edit, Index, Preview, PDF (QuestPDF fit theo khung preset, không cắt byte gốc).
5. **Kiểm thử & Vận hành (Phase 5)**:
   - Unit tests và regression tests đầy đủ. Cập nhật `MEDIA_AND_AI_OPERATIONS.md`.

## 2. Các file kế hoạch đã commit
- `docs/plans/261006-2350-unified-prompt-and-final-product-image/plan.md`
- `docs/plans/261006-2350-unified-prompt-and-final-product-image/phase-01-data-model-and-logging.md`
- `docs/plans/261006-2350-unified-prompt-and-final-product-image/phase-02-prompt-builder-and-translation.md`
- `docs/plans/261006-2350-unified-prompt-and-final-product-image/phase-03-flux-klein-generator.md`
- `docs/plans/261006-2350-unified-prompt-and-final-product-image/phase-04-final-product-and-surfaces.md`
- `docs/plans/261006-2350-unified-prompt-and-final-product-image/phase-05-verification-and-docs.md`
