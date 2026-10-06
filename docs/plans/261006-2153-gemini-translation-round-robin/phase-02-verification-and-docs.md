# Giai đoạn 2 — Hành vi, smoke và tài liệu vận hành

## Ngữ cảnh

- [Kế hoạch tổng](./plan.md) · [Giai đoạn 1](./phase-01-gemini-translator.md)
- Tests: `tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs`, `tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs`.
- Tài liệu hiện có: `docs/materials/MEDIA_AND_AI_OPERATIONS.md` (mô tả M2M100 đã lỗi thời so với default Llama trong code).

## Mục tiêu

Khóa chặt điều kiện fallback, chứng minh integration của hai handler tạo/tạo lại, cập nhật tài liệu cấu hình an toàn. Không thêm test chỉ để assert DI/config text; test qua kết quả/hành vi gửi request.

## File map

| Thao tác | Đường dẫn | Mục đích |
|---|---|---|
| Sửa | `tests/RecipeCard.Web.Tests/GeminiRoundRobinPromptTranslatorTests.cs` | Mở rộng test cốt lõi từ giai đoạn 1: xoay key, response biên, exhaustion và lỗi. |
| Sửa | `tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs` | Giữ kiểm thử Llama fallback; xóa các test M2M100 ràng buộc nhánh đã bị xóa. |
| Sửa nếu cần | `tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs` | Duy trì các assertion consumer-visible: prompt tiếng Anh vào FLUX, fail dịch không tạo draft; thêm case tích hợp chỉ nếu test mới chưa phủ. |
| Sửa | `docs/materials/MEDIA_AND_AI_OPERATIONS.md` | Sơ đồ, config, hướng dẫn nguồn dịch Gemini → Llama, giới hạn quota và lỗi. |

## Kiểm thử bắt buộc

Dùng fake HTTP, key giả `k1`, `k2`, `k3`; header capture tại thời điểm request, không lưu `HttpRequestMessage` đã dispose; fake fallback đếm số lần gọi hoặc fake HTTP Cloudflare đủ trả bản dịch thật.

1. Gemini thành công ngay lần đầu: endpoint, model, header, system/user parts hợp lệ; text đã dọn sạch; không gọi Llama; tiếng Anh đến image generator.
2. `k1=429`, `k2=success`: thứ tự đúng, không gọi key 3/Llama. Hai yêu cầu liên tiếp bắt đầu `k1`, rồi `k2` dù yêu cầu đầu đã thử nhiều keys. Cho request song song, cursor trả vị trí bắt đầu khác nhau theo modulo mà không race (test deterministic, không yêu cầu thứ tự hoàn tất).
3. **15 × 429** với 3 key: thứ tự `[k1,k2,k3]×5`, ở lượt 14 chưa fallback; sau lượt 15 gọi Llama đúng một lần, lấy bản dịch Llama làm đầu vào cho FLUX. Nếu fallback Llama cũng lỗi, không gửi prompt thô cho FLUX/không tạo draft.
4. `1 key → 5×429`, `2 key → 10×429`, `3 key → 15×429`; N=0/>3/duplicates/whitespace phải báo lỗi cấu hình an toàn. Kiểm thử `429` vòng cuối rồi success ở lần 15 → **không** fallback.
5. `400/401/403/500`, lỗi mạng, malformed/blank/blocked response: không gọi key tiếp, Llama hay FLUX; không tiết lộ key/prompt trong thông báo. Caller cancellation không tiếp tục attempt, không fallback. Một fallback failure báo lỗi dịch, giữ state/draft theo behavior hiện tại.
6. Giữ/regression tests `GenerateAiImage` và `RegenerateAiImage`: trước FLUX luôn có English translation, `PromptSnapshot` lưu đúng final prompt; verify Cloudflare image generation path vẫn hoạt động bằng fake provider.

## Smoke sau khi implement

- `dotnet test tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj --filter "FullyQualifiedName~AiPromptTranslatorTests|FullyQualifiedName~GeminiRoundRobinPromptTranslatorTests|FullyQualifiedName~AiImageCandidateTests"`.
- `dotnet test tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj`.
- Chạy web app với config dev an toàn (không check-in key), hoặc fake outbound HTTP trong một harness ngắn hạn, đi qua luồng tạo và tạo lại ảnh; quan sát bản dịch được dùng để tạo candidate và `PromptSnapshot`, không thêm ảnh vào media trước khi Accept. Nếu credentials thực không có, nêu giới hạn provider thật; không thay bằng lời tuyên bố đã test thật.

## Tài liệu vận hành

- Thay dòng kiến trúc M2M100 và ví dụ config cũ; ghi rõ Cloudflare FLUX **vẫn** sinh ảnh, Gemini chỉ dịch, Cloudflare Llama chỉ dịch dự phòng.
- Hướng dẫn thêm tối đa 3 key ở .NET User Secrets / biến môi trường `Gemini__ApiKeys__0..2`; tuyệt đối không commit vào `appsettings.json`. Ghi rõ 3 key chung project không tăng quota (Google quota theo project).
- Ghi đúng policy: 5 vòng trọn vẹn per request chỉ trên `429`; success dừng ngay; lỗi khác báo ngay, không fallback; mọi vòng `429` mới dùng Llama một lần. Nêu 15 requests có thể chậm và tốn quota, không cam kết tốc độ.
- Cập nhật bảng troubleshooting (Gemini config sai/429/Llama lỗi), gỡ mô tả cũ nói 'mọi lỗi dịch đều hủy tạo ảnh' trong trường hợp 429 có Llama thành công.

## Hoàn thành khi

- [x] Tests hành vi trước sửa fail đúng lý do và sau sửa pass; chạy toàn bộ suite.
- [x] Có smoke bao phủ đường đi dịch → FLUX → draft (hoặc báo rõ giới hạn môi trường ngoài).
- [x] Tài liệu khớp mã, không nêu model M2M100 như default/fallback hiện tại và không để key trong repo.
