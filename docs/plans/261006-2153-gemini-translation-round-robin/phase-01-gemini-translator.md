# Giai đoạn 1 — Cấu hình, dịch Gemini và chuyển DI

## Ngữ cảnh

- [Kế hoạch tổng](./plan.md)
- Mã: `src/RecipeCard.Web/Services/IAiPromptTranslator.cs`, `CloudflareWorkersAiPromptTranslator.cs`, `CloudflareOptions.cs`, `Program.cs`, `Pages/Recipes/Edit.cshtml.cs`.
- API tham chiếu: [Google `models.generateContent`](https://ai.google.dev/api/generate-content); [Google rate limits](https://ai.google.dev/gemini-api/docs/rate-limits).

## Mục tiêu

Đổi implementation của `IAiPromptTranslator`, **không đổi interface hay ảnh**. Hai typed HTTP clients độc lập: Gemini chính, Cloudflare translator cụ thể dự phòng. Cursor singleton chia sẻ điểm bắt đầu giữa mọi lần resolve translator trong một tiến trình.

## File map

| Thao tác | Đường dẫn | Mục đích |
|---|---|---|
| Tạo | `src/RecipeCard.Web/Services/GeminiOptions.cs` | Bind danh sách 1–3 key, validate không rỗng/trùng. |
| Tạo | `src/RecipeCard.Web/Services/GeminiKeyCursor.cs` | Singleton counter thread-safe để lấy start index, không lưu/ghi log key. |
| Tạo | `src/RecipeCard.Web/Services/GeminiRoundRobinPromptTranslator.cs` | REST Gemini, tối đa 5×N calls khi `429`, fallback Cloudflare chỉ sau exhaust. |
| Sửa | `src/RecipeCard.Web/Program.cs` | Bind options; giữ Cloudflare FLUX typed client; bind Cloudflare translator bằng concrete; bind Gemini translator bằng `IAiPromptTranslator`. |
| Sửa | `src/RecipeCard.Web/Services/CloudflareOptions.cs` | Xóa `TranslationModel` đã lỗi thời, giữ nguyên `Model` cho FLUX. |
| Sửa | `src/RecipeCard.Web/Services/CloudflareWorkersAiPromptTranslator.cs` | Cố định Llama fallback, xóa nhánh M2M100 và response parsing không còn dùng. |
| Tạo | `tests/RecipeCard.Web.Tests/GeminiRoundRobinPromptTranslatorTests.cs` | Viết test đỏ trước khi implement adapter; bổ sung edge cases ở giai đoạn 2. |
| Không sửa | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs` | Hai handler đã dùng chung `BuildAndTranslatePromptAsync`. |
| Không sửa | `src/RecipeCard.Web/Services/CloudflareWorkersAiImageGenerator.cs` | Duy trì FLUX, credentials và payload cũ. |

## Hợp đồng kỹ thuật

1. Bind `Gemini:ApiKeys` từ User Secrets/env (`Gemini__ApiKeys__0`, `__1`, `__2`); cố định model `gemini-2.5-flash` trong adapter để tránh chọn nhầm model tạo ảnh. `GeminiOptions.Validate` ném lỗi cấu hình rõ ràng khi N=0, N>3, key whitespace hoặc trùng (so sánh ordinal); **không chứa giá trị key trong exception**. Không đưa key mẫu thực vào `appsettings.json`.
2. `GeminiKeyCursor.NextStart(int count)` thread-safe bằng `Interlocked`; thứ tự bắt đầu tuần tự `0,1,2,0...` cho N=3. Làm đúng modulo cả khi bộ đếm overflow (dùng phép tính an toàn), không dựa vào lifetime typed client. Một instance singleton; không dùng static toàn cục khó cô lập test.
3. Gemini REST: `POST /v1beta/models/gemini-2.5-flash:generateContent` với header `x-goog-api-key`, `Content-Type: application/json`; body gồm `systemInstruction.parts[0].text` hướng dẫn dịch prompt F&B như Cloudflare Llama, `contents[0].role = user`, `contents[0].parts[0].text = Vietnamese prompt`, `generationConfig` hạn chế độ dài đáp án nếu phù hợp contract. Parse text từ candidate đầu tiên `content.parts` (nối các text part theo thứ tự), trim dấu quote/markdown nếu adapter cũ đang làm vậy. Không đọc `thought` part làm bản dịch; không trả raw input khi response thiếu text.
4. Với từng request: validate input/options trước call; lấy `start = cursor.NextStart(N)` **một lần**; vòng `attempt = 0..(5*N-1)`, chọn `ApiKeys[(start + attempt) % N]`; tạo `HttpRequestMessage` mới cho mỗi attempt (header riêng), dispose request/response. Success + text hợp lệ → return ngay. Nếu `StatusCode == 429` → attempt kế, không chờ; nếu đã attempt cuối → gọi `_cloudflare.TranslateVietnameseToEnglishAsync(prompt, ct)` chính xác một lần.
5. Mọi HTTP status khác `429` → lỗi Gemini được phân loại thành thông báo an toàn, không xoay key, không gọi Cloudflare. Lỗi mạng, timeout, parse không hợp lệ, content bị chặn/rỗng → lỗi dịch an toàn. `OperationCanceledException` do caller hủy truyền thẳng; tuyệt đối không fallback khi cancel. Không đưa JSON lỗi từ provider, header, prompt, key vào thông báo người dùng.
6. DI: `AddSingleton<GeminiKeyCursor>()`, `Configure<GeminiOptions>(...)`, `AddHttpClient<CloudflareWorkersAiPromptTranslator>(...)` và `AddHttpClient<IAiPromptTranslator, GeminiRoundRobinPromptTranslator>(...)`; giữ riêng `AddHttpClient<IAiImageGenerator, CloudflareWorkersAiImageGenerator>`. Trong `CloudflareWorkersAiPromptTranslator`, cố định endpoint/model `@cf/meta/llama-3.1-8b-instruct`; xóa thuộc tính `CloudflareOptions.TranslationModel`, nhánh M2M100 và test tương ứng. Bỏ registration cũ `IAiPromptTranslator → Cloudflare...`.

## Thứ tự thực hiện

1. Viết test hành vi cốt lõi trong `GeminiRoundRobinPromptTranslatorTests.cs` theo TDD (success, 429 exhaustion, lỗi ngoài 429); chạy test thấy fail trước khi implement. Giai đoạn 2 bổ sung edge cases và regression.
2. Thêm options/cursor rồi adapter request/response, nối fallback; sửa DI một lần.
3. Chạy nhóm test tập trung sau khi implement; không test gọi Google thật hoặc đưa API key vào source.

## Rủi ro / bảo mật

- 5 vòng có thể gửi 15 calls cho một click; không retry mạng/500. Tránh dùng một `HttpRequestMessage` nhiều lần hoặc set API key trên `DefaultRequestHeaders` của HttpClient dùng chung.
- Cloudflare token vẫn cần cho FLUX và fallback; không log secrets, prompt hoặc provider response; đừng lộ key qua URL query.
- Multiple app instances sẽ có cursor riêng; không cần distributed coordination trong phạm vi dự án SQLite cục bộ.

## Hoàn thành khi

- [x] DI chỉ resolve Gemini translator từ `IAiPromptTranslator`; concrete Cloudflare còn được inject cho fallback.
- [x] `IAiImageGenerator`/image workflow không đổi.
- [x] Mọi call Gemini bị `429` → đúng `5×N` lượt, rồi đúng một fallback Llama; success dừng ngay; lỗi ngoài 429 không fallback.
