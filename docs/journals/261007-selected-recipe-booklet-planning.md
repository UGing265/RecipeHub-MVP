---
title: "Nhật ký lập kế hoạch PDF booklet công thức đã chọn"
date: 2026-10-07
type: planning-journal
status: completed
tags: [pdf, booklet, questpdf, planning]
---

# Nhật ký lập kế hoạch PDF booklet công thức đã chọn

## Context

Brainstorm và lập kế hoạch xuất một PDF booklet từ các công thức được chọn tại `Recipes/Index`, đồng thời giữ nguyên luồng xuất PDF đơn hiện có.

- Thiết kế: `docs/plans/261007-selected-recipe-booklet/design.md`
- Kế hoạch: `docs/plans/261007-selected-recipe-booklet/plan.md`
- Chi tiết triển khai: `docs/plans/261007-selected-recipe-booklet/phase-01-shared-pdf-model-and-media.md` đến `phase-04-regression-smoke-and-docs.md`

## What happened

- Chốt UX checkbox chọn **1–50** công thức đủ điều kiện; server truy vấn lại dữ liệu và luôn xuất theo `Id DESC`.
- Chốt booklet A4 ngang gồm cover, TOC có thể tự phân trang, rồi từng công thức bắt đầu ở page section mới.
- Cover theo tinh thần tài liệu Phê La, có logo chính thức và dòng `Xuất bởi R&D Recipe Hub`.
- Tách media pipeline dùng chung và đặt giới hạn rõ cho concurrency, timeout, byte, pixel, resize/compress, ngân sách batch, cancellation và temp cleanup.
- Tạo plan tổng và bốn phase; chưa sửa code, chưa chạy test/build.

## Decisions

- Dùng một QuestPDF document tổng hợp, không ghép nhiều PDF và không ghép phía trình duyệt.
- Checkbox chỉ eligible khi công thức có ít nhất một nguyên liệu và một bước; trạng thái client không phải authority.
- POST dùng antiforgery, validation all-or-nothing; không lọc hoặc sửa input âm thầm.
- Media chỉ decode frame đầu của ảnh động; pipeline có giới hạn tài nguyên và fallback từng ảnh để không làm hỏng cả booklet.
- Logo bắt buộc tại `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg`; không placeholder, không crop từ PDF tham khảo, không fallback chữ.

## Risks/dependencies

- Chưa có logo Phê La chính thức, nền trong suốt và có quyền sử dụng; đây là dependency chặn phát hành.
- Batch 50 công thức nhiều ảnh có rủi ro timeout, RAM và file lớn; cần smoke đo bằng pipeline bounded.
- Red-team đã sửa plan để: TOC tự phân trang thay vì giả định một trang; giới hạn ảnh động ở frame đầu; kiểm thử HTTP thật cho lỗi model binder; sau POST 400 chỉ giữ checked cho ID tồn tại **và còn eligible**, tách eligibility gốc khỏi trạng thái disable tạm do cap 50.

## Next

1. Cung cấp và xác minh asset logo chính thức.
2. Triển khai tuần tự Phase 1–4 theo checklist, giữ clean cutover sang composer/media factory dùng chung.
3. Chạy test tập trung và smoke batch 50 ở Phase 4, gồm TOC nhiều trang, animated frame limit, binder HTTP và phục hồi checkbox eligible.
4. Cập nhật tài liệu vận hành sau khi smoke đạt.
