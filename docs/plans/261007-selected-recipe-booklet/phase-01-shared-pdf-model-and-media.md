# Phase 1: Mapping PDF dùng chung và pipeline media có giới hạn

## Liên kết

- Kế hoạch: [plan.md](./plan.md)
- Thiết kế duyệt: [design.md](./design.md)
- Phase kế tiếp: [phase-02-booklet-document-and-brand.md](./phase-02-booklet-document-and-brand.md)

## Mục tiêu

Trích toàn bộ mapping `Recipe -> RecipePdfModel` và tải ảnh khỏi `PreviewModel` thành dịch vụ dùng chung. PDF đơn phải tiếp tục trả cùng status/result/tên file, còn pipeline mới phải an toàn khi dùng cho tối đa 50 recipe.

## File ảnh hưởng

Project root tuyệt đối: `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi`.

| Thao tác | Tương đối | Tuyệt đối |
|---|---|---|
| Modify | `src/RecipeCard.Web/RecipeCard.Web.csproj` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/RecipeCard.Web.csproj` |
| Modify | `src/RecipeCard.Web/Program.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Program.cs` |
| Create | `src/RecipeCard.Web/Services/PdfMediaLoader.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Services/PdfMediaLoader.cs` |
| Create | `src/RecipeCard.Web/Services/RecipePdfModelFactory.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Services/RecipePdfModelFactory.cs` |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs` |
| Modify | `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` |

## Hợp đồng dịch vụ

### `RecipePdfModelFactory.cs`

Định nghĩa các type cùng file để giữ API nhỏ:

```csharp
public interface IRecipePdfModelFactory
{
    Task<RecipePdfModelBatch> CreateAsync(
        IReadOnlyList<Recipe> recipes,
        CancellationToken cancellationToken);
}

public sealed class RecipePdfModelBatch : IAsyncDisposable
{
    public IReadOnlyList<RecipePdfModel> Models { get; }
    public ValueTask DisposeAsync();
}
```

- Input phải là entity graph đã load đủ `FinalMediaAsset`, `Ingredients.Ingredient`, `Steps.MediaAsset`.
- Giữ nguyên thứ tự input, ingredient theo thứ tự navigation hiện có, step theo `SortOrder`.
- Map `Title`, `GeneralNote`, `RecipePdfIngredientLine`, `RecipePdfStepLine`, `IsAiIllustration` giống logic hiện tại.
- Hero và ảnh step đều đi qua `PdfMediaLoader`; không còn `ReadAsByteArrayAsync` không giới hạn trong page model.
- `RecipePdfModelBatch` sở hữu thư mục temp và xóa idempotent khi dispose, kể cả khi factory lỗi giữa chừng.

### `PdfMediaLoader.cs`

- Nhận source remote (`DeliveryUrl`) hoặc local (`wwwroot/uploads/steps/{ImageFileName}`) cùng loại `Hero`/`Step`.
- Tạo thư mục `%TEMP%/recipe-card-pdf/{guid}` ngoài `wwwroot`; tên file ngẫu nhiên, không dùng tên/URL từ user.
- Remote:
  - dùng named client `MediaDelivery` với `HttpCompletionOption.ResponseHeadersRead`;
  - timeout riêng 10 giây qua linked CTS;
  - từ chối ngay nếu `Content-Length > 8 MiB`;
  - nếu không có/không tin `Content-Length`, copy qua stream đếm và dừng tại byte thứ `8 MiB + 1`;
  - chỉ chấp nhận dữ liệu mà ImageSharp nhận diện được là image.
- Local:
  - canonicalize path và chỉ đọc file được tạo từ đường dẫn `webroot/uploads/steps`; file mất/không đọc được là fallback;
  - kiểm tra `FileInfo.Length <= 8 MiB`, sau đó đi chung pipeline nhận diện/resize.
- Trước decode, `Image.Identify` và từ chối width/height không dương, mỗi chiều > 6.000 hoặc `width * height > 16.000.000` bằng phép tính `long`.
- Decode/resize bằng ImageSharp 4.1.2 với `DecoderOptions` chỉ nhận frame đầu và bỏ metadata; không decode toàn bộ GIF/WebP/APNG động:
  - hero `ResizeMode.Max`, box 1.200 × 750;
  - step `ResizeMode.Max`, box 900 × 600;
  - không upscale; flatten alpha lên nền trắng;
  - JPEG quality 82, rồi 72/62; nếu vẫn >750 KiB, scale 85% lặp tới cạnh dài 480; vẫn lớn thì fallback.
- Dùng semaphore độc lập: tối đa 4 request/read remote và 2 decode/resize. Luôn release trong `finally`.
- Ngân sách output: 64 MiB encoded và 75.000.000 pixel. Commit kết quả theo recipe `Id DESC`, hero trước rồi step `SortOrder`, không theo thứ tự task hoàn tất. Kết quả vượt ngân sách bị xóa và coi như ảnh thiếu.
- Deadline media batch 60 giây: hủy media chưa xong và tiếp tục với model không ảnh. Phân biệt `RequestAborted`: cancellation từ caller phải ném `OperationCanceledException`, không được nuốt.
- Bắt riêng lỗi mạng/HTTP/decode/file; log warning không chứa query string/token và trả `null`. Không dùng `catch {}`.

## Các bước triển khai

- [x] **1. Thêm dependency xử lý ảnh.** Trong `RecipeCard.Web.csproj`, thêm `SixLabors.ImageSharp` version `4.1.2`.
- [x] **2. Viết test factory trước.** Tạo ảnh thật bằng ImageSharp và fake `HttpMessageHandler`; test output dimension, file temp, cleanup, fallback và cancellation bằng hành vi.
- [x] **3. Cài `PdfMediaLoader`.** Áp dụng toàn bộ limit trong bảng ở `plan.md`; dùng stream, không tạo bản sao byte không cần thiết.
- [x] **4. Cài `RecipePdfModelFactory`.** Lập danh sách media theo thứ tự ổn định, map text trước, gắn đường dẫn ảnh chuẩn hóa sau khi loader hoàn tất.
- [x] **5. Đăng ký DI.** `Program.cs`: `AddScoped<IRecipePdfModelFactory, RecipePdfModelFactory>()`, `AddScoped<PdfMediaLoader>()`; named client `MediaDelivery` đặt timeout 10 giây.
- [x] **6. Chuyển Preview sang factory.** Giữ `LoadRecipeAsync`; thay block mapping/tải media bằng `await using var batch = await factory.CreateAsync([recipe], HttpContext.RequestAborted)`, rồi sinh `RecipePdfDocument(batch.Models.Single())` trước khi dispose.
- [x] **7. Giữ hợp đồng PDF đơn.** Không đổi route, `NotFound`, thông báo recipe chưa sẵn sàng, content type hoặc slug filename.
- [x] **8. Cập nhật test cũ.** Khởi tạo `PreviewModel` với factory thật theo constructor mới; giữ các assertion `%PDF-`, landscape, title, missing local image không throw.
## Test hành vi bắt buộc

`RecipePdfModelFactoryTests.cs` phải có ít nhất:

1. `CreateAsync_maps_ingredients_steps_and_media_in_input_order` — map hai recipe và chứng minh thứ tự input + `SortOrder` được giữ.
2. `Remote_media_never_exceeds_four_concurrent_requests` — fake handler giữ request tại barrier, đo peak active thực tế `<= 4`.
3. `Decode_resize_never_exceeds_two_concurrent_images` — inject seam nội bộ hoặc observer test-only để đo peak, không dùng sleep-only assertion.
4. `Oversized_content_length_and_stream_without_length_are_omitted` — cả hai đường đều không cấp quá 8 MiB và model vẫn được tạo.
5. `Image_over_dimension_or_pixel_limit_is_omitted_without_failing_recipe`.
5a. `Animated_image_decodes_only_first_frame` — fixture GIF/WebP nhiều frame chỉ tạo một JPEG tĩnh trong giới hạn và không nhân peak memory theo số frame.
6. `Hero_and_step_are_resized_within_their_boxes_and_output_cap` — đọc file temp bằng `Image.Identify` và `FileInfo.Length`.
7. `Timed_out_or_invalid_remote_image_becomes_null` — model còn text đầy đủ.
8. `Caller_cancellation_propagates_and_temp_directory_is_removed`.
9. `Disposing_batch_removes_all_normalized_files`.
10. Regression trong `RecipePdfExportTests`: PDF đơn hợp lệ, recipe thiếu dữ liệu vẫn redirect với message cũ, file local thiếu vẫn xuất.

Không chạy test/build riêng ở phase này; Phase 4 chạy toàn bộ sau khi các phase cùng hoàn tất.

## Rủi ro và cách chặn

- **Decompression bomb:** identify kích thước trước decode, giới hạn 16 MP, decode concurrency 2.
- **Memory tăng theo số ảnh:** không giữ ảnh gốc hoặc decoded frame sau encode; model giữ file path; có encoded/pixel budget toàn batch.
- **Temp leak:** batch là owner duy nhất, page handler dùng `await using`, test cả success/cancel/exception.
- **Thay đổi PDF đơn ngoài ý muốn:** Preview chỉ thay nguồn model; response contract và test regression giữ nguyên.

## Tiêu chí hoàn thành Phase 1

- Không còn code mapping ingredient/step hoặc tải media trong `PreviewModel`.
- Factory dùng được cho một hoặc nhiều recipe, giữ đúng thứ tự input.
- Mọi limit, timeout, cancellation và cleanup có test hành vi tương ứng.
- PDF đơn giữ nguyên route/status/message/content type/filename và vẫn fallback khi ảnh lỗi.
