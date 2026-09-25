# Technical Journal: Cloudinary Media Library & Cloudflare Workers AI FLUX.1 Demo

- **Date:** 2026-09-25
- **Branch:** main
- **Plan:** `plans/260925-1446-cloudinary-media-library/plan.md`
- **Status:** Completed

---

## 1. Executive Summary

Triển khai trọn vẹn giải pháp quản lý thư viện hình ảnh tập trung (Cloudinary Media Library) và quy trình duyệt ứng viên minh họa AI (Cloudflare Workers AI FLUX.1 Schnell) cho ứng dụng R&D Recipe Hub theo ngôn ngữ thiết kế Monochrome (Mobbin Standard).

Toàn bộ 4 giai đoạn đã hoàn tất:
1. **Phase 1 (External image foundations):** Tích hợp CloudinaryDotNet SDK, `ImageValidator` đa định dạng (magic bytes, size <= 5MB), `CloudflareWorkersAiImageGenerator` (FLUX.1 Schnell REST API).
2. **Phase 2 (Asset data & legacy cutover):** Thêm thực thể `MediaAsset`, `AiImageDraft`, staged EF Core migration `AddMediaAssetAndAiDraft`, `AiDraftFileStore` (`App_Data/ai-drafts`), và CLI tool `--migrate-step-images --confirm`.
3. **Phase 3 (Media library & recipe integration):** Xây dựng trang `/Media/Index`, bộ lọc nguồn và trạng thái xóa, chọn ảnh tái sử dụng trong `Edit.cshtml`, nạp ảnh remote bằng `HttpClient` ("MediaDelivery") vào QuestPDF.
4. **Phase 4 (AI candidate approval demo):** `AiImagePromptBuilder`, luồng tạo/xem trước/tạo lại/bỏ/chấp nhận ứng viên ảnh AI (chỉ upload Cloudinary khi Accept), nhãn "AI minh họa" trên mọi bề mặt giao diện, và `AiDraftCleanupHostedService`.

---

## 2. Key Architectural Decisions

1. **Staging Candidate Outside Cloudinary:**
   - Ứng viên AI tạo từ FLUX.1 Schnell được lưu tạm dưới `App_Data/ai-drafts/{guid}.jpg`, không lưu trên Cloudinary và không đưa vào `wwwroot`.
   - Chỉ khi người dùng bấm "Chấp nhận ảnh này", backend mới upload dữ liệu bytes lên Cloudinary, tạo bản ghi `MediaAsset(SourceType = AiIllustration)` và liên kết bước công thức.

2. **Reference Integrity & Non-destructive Step Deletion:**
   - Xóa bước thực hiện chỉ ngắt liên kết `MediaAssetId`, không xóa `MediaAsset` dùng chung.
   - Thư viện ảnh `/Media/Index` ngăn chặn xóa ảnh nếu đang có $\ge 1$ bước công thức tham chiếu.
   - Cơ chế bù trừ (compensation): nếu lưu CSDL thất bại sau khi đã upload remote, hệ thống tự động gọi xóa ảnh trên Cloudinary.

3. **Legacy Migration Safety:**
   - CLI lệnh `--migrate-step-images` yêu cầu cờ `--confirm` để tránh chạy nhầm.
   - Từng bước thực hiện được xử lý độc lập kèm báo cáo chi tiết: thành công, thiếu file, file hỏng, lỗi provider, lỗi CSDL.

---

## 3. Verification & Quality Metrics

- **Unit & Integration Tests:** 50/50 test cases passed (100% pass rate).
- **Database Migrations:** Migration `20260925084333_AddMediaAssetAndAiDraft` áp dụng thành công trên `recipe-card.db`.
- **Legacy Conversion Smoke Test:** Thực thi lệnh `dotnet run -- --migrate-step-images --confirm` chuyển đổi thành công 100% bước có ảnh cũ.
