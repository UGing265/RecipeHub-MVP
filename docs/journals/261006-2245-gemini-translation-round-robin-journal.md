# Nhật ký triển khai: Dịch prompt bằng Gemini 2.5 Flash Round-Robin, Fallback Llama

- **Thời gian**: 2026-10-06
- **Kế hoạch**: `docs/plans/261006-2153-gemini-translation-round-robin/plan.md`
- **Mục tiêu**: Thay thế nguồn dịch prompt chính sang Gemini 2.5 Flash với cơ chế xoay 1–3 keys (tối đa 5 vòng cho lỗi HTTP 429), duy nhất fallback sang Cloudflare Llama 1 lần khi kiệt 5 vòng 429.

## 1. Các thành phần đã triển khai
1. `GeminiOptions.cs`: Cấu hình danh sách key `Gemini:ApiKeys`, ràng buộc 1–3 keys, kiểm tra không rỗng/trùng ordinal, exception không chứa key bí mật.
2. `GeminiKeyCursor.cs`: Singleton thread-safe cursor dùng `Interlocked` chia sẻ điểm bắt đầu tuần tự giữa các requests.
3. `GeminiRoundRobinPromptTranslator.cs`:
   - REST call `POST /v1beta/models/gemini-2.5-flash:generateContent` qua header `x-goog-api-key`.
   - Vòng lặp tối đa 5 vòng × N keys (15 lần với 3 keys) khi và chỉ khi gặp HTTP 429.
   - Thành công tại bất kỳ vòng nào lập tức dừng và trả về text đã sanitize.
   - Sau khi hết 5 vòng đều 429, fallback Cloudflare Llama đúng 1 lần duy nhất.
   - Lỗi non-429 (400, 401, 403, 5xx, timeout, response rỗng): dừng ngay, báo lỗi, không fallback.
   - Phân biệt rõ ngoại lệ timeout (`OperationCanceledException` khi caller token chưa hủy) sang `InvalidOperationException` thông báo an toàn; caller cancellation được rethrow nguyên bản.
4. `CloudflareOptions.cs` & `CloudflareWorkersAiPromptTranslator.cs`:
   - Loại bỏ model cấu hình M2M100 cũ, cố định model fallback `@cf/meta/llama-3.1-8b-instruct`.
   - Chuẩn hóa thông báo lỗi không để lộ upstream message/token/prompt.
   - Xử lý timeout rõ ràng tương tự.
5. `Program.cs`: Đăng ký DI singleton cursor, options, concrete Cloudflare translator và bind `IAiPromptTranslator` tới Gemini translator.
6. `docs/materials/MEDIA_AND_AI_OPERATIONS.md`: Tài liệu hóa cơ chế xoay key, cấu hình User Secrets/env, và giới hạn quota project của Google.

## 2. Kiểm thử xác nhận
- Toàn bộ 112/112 tests trong `RecipeCard.Web.Tests` đều PASSED.
- Bao phủ:
  - 15 lần 429 xoay đúng 5 vòng rồi gọi Llama 1 lần.
  - Lần thứ 14 429 chưa fallback.
  - Success ở bất kỳ lượt nào dừng ngay.
  - Lỗi non-429 không retry và không fallback.
  - Thread-safe cursor phân phối start index an toàn.
  - Timeout được xử lý an toàn không leak secret message.
  - Caller cancellation được tôn trọng.
