---
title: "Dịch prompt bằng Gemini 2.5 Flash, xoay tối đa 3 key, dự phòng Llama"
description: "Thay nguồn dịch prompt minh họa ảnh bằng Gemini round-robin 5 vòng và chỉ dùng Cloudflare Llama khi mọi lượt đều bị 429."
status: completed
priority: P2
effort: 6h
branch: feat/pdf-layout
tags: [feature, backend, api]
blockedBy: []
blocks: []
created: 2026-10-06
---

# Dịch prompt bằng Gemini với dự phòng Llama

## Mục tiêu và phạm vi

- `GenerateAiImage` và `RegenerateAiImage` vẫn tạo prompt Việt, dịch sang Anh, ghép guideline tiếng Anh, rồi dùng Cloudflare FLUX. Không thay `IAiImageGenerator`, candidate lifecycle, UI, SQLite, Cloudinary, PDF.
- Nguồn dịch chính: Gemini `gemini-2.5-flash` bằng REST server-side. Cấu hình **1–3 key** trong `Gemini:ApiKeys`; với 3 key, mỗi yêu cầu dịch thử tối đa **5 vòng × 3 key = 15 lần gọi**. Bất kỳ lượt thành công nào dừng ngay.
- `429` là điều kiện **duy nhất** để thử key kế và, nếu **mọi lượt của đủ 5 vòng** đều `429`, gọi Cloudflare Llama **một lần**. Không chuyển Llama giữa chừng, không dùng prompt Việt làm đầu vào FLUX. Không sleep giữa lượt (có thể chậm khi 15 lượt liên tiếp bị giới hạn).
- `400`, `401`, `403`, các lỗi 4xx khác, 5xx, lỗi mạng/timeout, phản hồi thiếu text hoặc hủy yêu cầu: **không phải 429**; báo lỗi dịch ngay, không xoay tiếp và không fallback. Tránh che lỗi cấu hình/hợp đồng; chính sách này là quyết định thiết kế vì người dùng chỉ yêu cầu fallback khi hết rate limit. Nếu Llama lỗi: báo lỗi, không gọi FLUX.
- Với số key `N=1..3`, tối đa `5×N` lượt; mỗi yêu cầu lấy một vị trí bắt đầu round-robin kế tiếp dùng chung giữa các request trong cùng tiến trình; trong một request, thứ tự `(start + attempt) % N`. Có 3 key và bắt đầu ở key 2: `2→3→1` lặp 5 vòng. Sang request sau bắt đầu key 3 (không phụ thuộc số lượt đã gọi trong request trước). Không giới thiệu phân phối đa tiến trình hoặc quota pooling giả: Google tính rate limit theo **project**, không theo key.

## Hiện trạng / phụ thuộc

- `src/RecipeCard.Web/Program.cs` đang bind `IAiPromptTranslator` trực tiếp tới `CloudflareWorkersAiPromptTranslator`; ảnh đã bind riêng tới `CloudflareWorkersAiImageGenerator`.
- `EditModel.BuildAndTranslatePromptAsync` dùng một interface chung cho cả tạo/tạo lại. `CloudflareOptions.TranslationModel` hiện mặc định Llama nhưng vẫn cho override sang M2M100; kế hoạch loại bỏ lựa chọn model dịch cũ và cố định Cloudflare adapter làm **Llama fallback**, để cấu hình cũ không âm thầm đổi provider dự phòng.
- `docs/materials/MEDIA_AND_AI_OPERATIONS.md` hiện ghi M2M100 mặc dù code đang mặc định Llama: cập nhật mục kiến trúc, cấu hình và vận hành theo luồng thật.
- Scan các plan cũ: `260923-2248-recipe-card-core` và `260925-1446-cloudinary-media-library` có `status: completed`; `260926-vietnamese-ai-prompt-translation` không có frontmatter nhưng toàn bộ implementation/verification checklist đã đánh dấu xong và các service/test tương ứng hiện diện. Đây là kế hoạch kế tiếp, **không có plan chưa hoàn tất chặn**; không sửa lại lịch sử kế hoạch cũ.
- Google REST: [`models.generateContent`](https://ai.google.dev/api/generate-content) và [rate limits theo project](https://ai.google.dev/gemini-api/docs/rate-limits). Endpoint `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent`, header `x-goog-api-key`, JSON `systemInstruction`, `contents`, đọc `candidates[].content.parts[].text` (chỉ text hợp lệ, không chấp nhận rỗng).

## Thiết kế tối thiểu

```text
EditModel → IAiPromptTranslator (GeminiRoundRobinPromptTranslator)
              ├─ Gemini REST / x-goog-api-key (1–3 key, 5 vòng, chỉ 429)
              └─ CloudflareWorkersAiPromptTranslator (chỉ sau 5 vòng đều 429)
          → AiImagePromptBuilder → IAiImageGenerator (Cloudflare FLUX không đổi)
```

- `GeminiOptions` chứa danh sách key; adapter dùng cố định `gemini-2.5-flash`; bind từ config server, xác thực 1–3 key không rỗng và không trùng (tránh lặp cùng key giả làm 3). Không hardcode/commit secrets. Ưu tiên .NET User Secrets lúc dev, biến môi trường `Gemini__ApiKeys__0` … `__2` lúc deploy. Cloudflare account/token tiếp tục dành cho FLUX và Llama; xóa `Cloudflare:TranslationModel` lỗi thời, adapter fallback gọi Llama cố định.
- `GeminiRoundRobinPromptTranslator` sở hữu request/response Gemini, chính sách 429 và fallback; inject Cloudflare translator cụ thể (typed HttpClient riêng). Chọn request-start bằng một singleton `GeminiKeyCursor` thread-safe (`Interlocked`, wrap modulo) để nhiều request/scoped typed clients vẫn chia sẻ chuỗi bắt đầu; không có trạng thái key bị vô hiệu hóa/cooldown vì yêu cầu lặp đủ 5 vòng kể cả 429.
- Cùng chỉ dẫn dịch F&B hiện có ở nhánh Llama (`đường nước` → `sugar syrup`, `đá viên` → `ice cubes`, chỉ xuất mô tả hành động tiếng Anh) áp dụng cho Gemini qua `systemInstruction`. Không mở rộng nhiệm vụ dịch sang phần khác của ứng dụng.
- Không in key, full prompt hay response chứa nội dung nhạy cảm vào log/exception. Dùng header thay vì URL query để key không nằm trong URL; lỗi hiển thị phân biệt Gemini/Llama và không chứa credential. Cancellation phải truyền tới mọi request; đã cancel không gọi provider tiếp.

## Các giai đoạn

| Giai đoạn | Nội dung | Trạng thái |
|---|---|---|
| 1 | [Cấu hình, adapter Gemini và chuyển DI](./phase-01-gemini-translator.md) | Complete |
| 2 | [Kiểm thử hành vi và tài liệu vận hành](./phase-02-verification-and-docs.md) | Complete |
## Tiêu chí hoàn thành

1. Với 3 key, 15 phản hồi `429` theo đúng 5 vòng mới tạo **1** yêu cầu Llama; lần thứ 14 `429` chưa gọi Llama; success ở bất kỳ lượt nào dừng, FLUX nhận bản dịch tiếng Anh.
2. Mỗi request mới bắt đầu từ key kế; các request đồng thời không cùng đọc/ghi cursor không an toàn. 1/2/3 key hoạt động; ngoài khoảng hoặc trùng key báo cấu hình sai, không rơi về nhà cung cấp khác một cách im lặng.
3. Lỗi không phải `429`, hủy, response rỗng: không gọi thêm key/Llama/FLUX. Llama lỗi sau exhaustion: không gọi FLUX. Luồng ảnh, draft, upload, prompt snapshot giữ nguyên.
4. Test cụ thể cho chính sách xoay/fallback và contract Gemini; test hiện có và smoke chạy web với fake provider hoặc môi trường dev có khóa hợp lệ. Không bắt buộc khóa thật trong CI.
5. Tài liệu `docs/materials/MEDIA_AND_AI_OPERATIONS.md` phản ánh model thực, lưu key an toàn, hành vi 429, giới hạn theo project và cách xác minh.

## Quyết định/rủi ro

- Không retry `429` với backoff vì người dùng chọn **5 vòng liên tục**; 15 lượt đều 429 có thể làm UI chờ và tăng số request vô ích. Không khẳng định 3 key cùng project có 3 quota.
- Không fallback cho lỗi ngoài `429`: nhanh thấy key sai, payload sai hoặc lỗi nhà cung cấp; đổi policy về sau cần quyết định riêng.
- Key đích không có trong repo; triển khai và test được với fake HTTP, kiểm tra end-to-end Google/Cloudflare thật cần credentials thuộc quyền chủ sở hữu.
