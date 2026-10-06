# Phase 3: Generator FLUX.2 Klein 4B & Kích thước Preset

## 1. Mục tiêu
- Nâng cấp `IAiImageGenerator` từ nhận chuỗi đơn giản sang nhận request DTO có kích thước và preset.
- Thay đổi `CloudflareWorkersAiImageGenerator` sang model `@cf/black-forest-labs/flux-2-klein-4b` sử dụng `multipart/form-data`.
- Ánh xạ 4 preset tỷ lệ sang kích thước thực tế (width × height) trong khoảng 256–1920 px.
- Giải mã ảnh base64 từ Cloudflare Workers AI và lưu tạm vào file nháp.

## 2. Thay đổi chi tiết

### 2.1. Request DTO và Helper Preset
- **`src/RecipeCard.Web/Services/AiImageGenerationRequest.cs`**:
  Tạo DTO mới:
  ```csharp
  public record AiImageGenerationRequest(
      string Prompt,
      AspectRatioPreset Preset,
      int Width,
      int Height,
      long? Seed = null);
  ```
- **`src/RecipeCard.Web/Services/AspectRatioPresetExtensions.cs`**:
  Phương thức ánh xạ kích thước:
  ```csharp
  public static class AspectRatioPresetExtensions
  {
      public static (int Width, int Height) ToDimensions(this AspectRatioPreset preset) => preset switch
      {
          AspectRatioPreset.Square1x1 => (1024, 1024),
          AspectRatioPreset.StandardLandscape4x3 => (1024, 768),
          AspectRatioPreset.WideLandscape16x9 => (1280, 720),
          AspectRatioPreset.Portrait4x5 => (768, 960),
          _ => throw new ArgumentOutOfRangeException(nameof(preset))
      };

      public static string ToDisplayName(this AspectRatioPreset preset) => preset switch
      {
          AspectRatioPreset.Square1x1 => "Vuông (1:1)",
          AspectRatioPreset.StandardLandscape4x3 => "Ngang chuẩn (4:3)",
          AspectRatioPreset.WideLandscape16x9 => "Ngang rộng (16:9)",
          AspectRatioPreset.Portrait4x5 => "Dọc (4:5)",
          _ => preset.ToString()
      };
  }
  ```

### 2.2. Giao diện và Client sinh ảnh
- **`src/RecipeCard.Web/Services/IAiImageGenerator.cs`**:
  Cập nhật signature:
  ```csharp
  public interface IAiImageGenerator
  {
      Task<byte[]> GenerateImageBytesAsync(AiImageGenerationRequest request, CancellationToken cancellationToken = default);
  }
  ```
- **`src/RecipeCard.Web/Services/CloudflareOptions.cs`**:
  - Đổi default `Model` sang `"@cf/black-forest-labs/flux-2-klein-4b"`.
- **`src/RecipeCard.Web/Services/CloudflareWorkersAiImageGenerator.cs`**:
  - Gửi request dưới dạng `MultipartFormDataContent`:
    - Thêm field `prompt` (chuỗi văn bản tiếng Anh hoàn chỉnh).
    - Thêm field `width` (int: 768..1280).
    - Thêm field `height` (int: 720..1024).
    - Thêm field `seed` nếu có giá trị.
    - **Không gửi** field `steps` vì model Klein 4B cố định 4 bước.
  - Phân tích response JSON từ Cloudflare:
    - Nếu API trả JSON có `result.image` dạng base64: giải mã `Convert.FromBase64String`.
    - Nếu API trả trực tiếp binary `image/jpeg` hoặc `image/png`: đọc trực tiếp stream bytes.
  - Kiểm tra magic bytes của mảng byte (JPEG, PNG, WebP) và chặn nếu dữ liệu không phải ảnh hợp lệ.
  - Không log nội dung `request.Prompt` hoặc raw base64. Chỉ log kích thước `width`, `height`, `preset`, thời gian và dung lượng ảnh.

## 3. Tiêu chí nghiệm thu Phase 3
- Fake HTTP test xác nhận: request gửi đi là `multipart/form-data`, chứa các part `prompt`, `width`, `height`; không chứa `steps`.
- Unit test kiểm chứng: 4 preset sinh đúng 4 cặp width/height theo bảng đặc tả.
- Unit test kiểm chứng: response base64 từ Cloudflare được giải mã thành byte array đúng chuẩn ảnh.
- Log console không chứa prompt text hoặc base64 string.
