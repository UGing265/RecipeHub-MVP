# Tài liệu vận hành và kiến trúc: Media Library & Tích hợp AI

Tài liệu này cung cấp hướng dẫn chi tiết về mặt kỹ thuật, kiến trúc và vận hành cho các tính năng mới được triển khai trong hệ thống **R&D Recipe Hub**:
1. **Lưu trữ đám mây Cloudinary** (`CloudinaryImageStorageService`).
2. **Quy trình sinh ảnh ứng viên bằng Cloudflare Workers AI** (Mô hình `@cf/black-forest-labs/flux-1-schnell` với cơ chế ứng viên tạm thời 30 phút).
3. **Thư viện tài nguyên số tập trung** (`/Media/Index` theo ngôn ngữ thiết kế Mobbin).
4. **Công cụ CLI di chuyển dữ liệu ảnh bước thực hiện** (`--migrate-step-images --confirm`).

---

## 1. Kiến trúc tổng thể và luồng dữ liệu

### 1.1 Sơ đồ tương tác thành phần

```
+-----------------------------------------------------------------------------------+
|                                  TRÌNH DUYỆT (WEB UI)                             |
|                                                                                   |
|  [ Thư viện ảnh: /Media/Index ]         [ Chỉnh sửa công thức: /Recipes/Edit ]    |
|   - Tải ảnh trực tiếp lên Cloudinary      - Chọn ảnh từ Thư viện                  |
|   - Lọc: Tất cả / Chụp / AI / Lỗi xóa    - Tải ảnh thật trực tiếp                 |
|   - Khóa xóa nếu đang có bước dùng        - Tạo ảnh AI ứng viên (Candidate Demo)  |
+--------------------------+------------------------------------+-------------------+
                           |                                    |
                           v                                    v
+-----------------------------------------------------------------------------------+
|                             ASP.NET CORE WEB APPLICATION                          |
|                                                                                   |
  +---------------------------+  +-----------------------------------------------+
  | IImageValidator           |  | AiImagePromptBuilder (Tạo prompt Việt)        |
  | - Magic bytes check       |  | - Ngữ cảnh công thức, bước, chỉ dẫn, brief    |
  | - Max 5MB (.jpg/.png/.webp)|  +-----------------------+-----------------------+
  | - Anti-path traversal     |                          |
  +---------------------------+                          v
               |                 +-----------------------------------------------+
               |                 | IAiPromptTranslator                           |
               |                 | (CloudflareWorkersAiPromptTranslator)         |
               |                 | - Model: @cf/meta/m2m100-1.2b                 |
               |                 | - Dịch prompt tiếng Việt sang tiếng Anh       |
               |                 +-----------------------+-----------------------+
               |                                         |
               |                                         v
               |                 +-----------------------------------------------+
               |                 | AiImagePromptBuilder (Ghép phong cách ảnh)    |
               |                 | - Bổ sung English style guideline cho FLUX    |
               |                 +-----------------------+-----------------------+
               |                                         |
               |                                         v
               |                 +-----------------------------------------------+
               |                 | IAiImageGenerator                             |
               |                 | (CloudflareWorkersAiImageGenerator)           |
               |                 | - REST API v4, Bearer token                   |
               |                 | - Model: @cf/black-forest-labs/flux-1-schnell |
               |                 +-----------------------+-----------------------+
|               |                                        v                          |
|               |               +-------------------------------------------------+ |
|               |               | IAiDraftFileStore & AiDraftCleanupHostedService | |
|               |               | - Tệp tạm tại App_Data/ai-drafts                | |
|               |               | - Hạn 30 phút (ExpiresUtc), dọn dẹp mỗi 10 phút | |
|               |               +------------------------+------------------------+ |
|               |                                        |                          |
|               |                        (Khi người dùng bấm "Chấp nhận" / Accept)   |
|               |                                        |                          |
|               +-----------------------+----------------+                          |
|                                       v                                           |
|               +-------------------------------------------------+                 |
|               | IImageStorageService                            |                 |
|               | - CloudinaryImageStorageService (khi có config) |                 |
|               | - ImageStorageService (fallback local wwwroot)  |                 |
|               +-----------------------+-------------------------+                 |
|                                       |                                           |
+---------------------------------------|-------------------------------------------+
                                        |
                 +----------------------+----------------------+
                 |                                             |
                 v                                             v
  +-------------------------------+             +-------------------------------+
  |       CLOUDINARY CDN          |             |       SQLITE DATABASE         |
  |  - Lưu trữ ảnh an toàn        |             |       (recipe-card.db)        |
  |  - DeliveryUrl (HTTPS)        |             |  - MediaAssets (khóa chính)   |
  |  - ProviderPublicId (để xóa)  |             |  - AiImageDrafts (lịch sử)    |
  |  - Phân phối toàn cầu         |             |  - RecipeSteps (MediaAssetId) |
  +-------------------------------+             +-------------------------------+
```

---

## 2. Cấu hình hệ thống (Configuration)

Cấu hình được quản lý qua `appsettings.json`, `appsettings.Development.json` hoặc biến môi trường:

```json
{
  "ConnectionStrings": {
    "RecipeDb": "Data Source=recipe-card.db"
  },
  "Cloudinary": {
    "CloudName": "your-cloud-name",
    "ApiKey": "your-api-key",
    "ApiSecret": "your-api-secret"
  },
  "Cloudflare": {
    "AccountId": "your-cloudflare-account-id",
    "ApiToken": "your-cloudflare-api-token",
    "Model": "@cf/black-forest-labs/flux-1-schnell",
    "TranslationModel": "@cf/meta/m2m100-1.2b"
  },
  "QuestPdf": {
    "LicenseType": "Community"
  }
}
```

### 2.1 Biến môi trường tương đương (Production / Container)
- `Cloudinary__CloudName`
- `Cloudinary__ApiKey`
- `Cloudinary__ApiSecret`
- `Cloudflare__AccountId`
- `Cloudflare__ApiToken`
- `Cloudflare__Model` (Mặc định: `@cf/black-forest-labs/flux-1-schnell`)
- `Cloudflare__TranslationModel` (Mặc định: `@cf/meta/m2m100-1.2b`)
### 2.2 Cơ chế Fallback thông minh
- **Dịch vụ lưu trữ**: Nếu `Cloudinary:CloudName` để trống hoặc không tồn tại trong cấu hình, `Program.cs` tự động giải quyết `IImageStorageService` thành `ImageStorageService` (lưu trữ tệp vào thư mục `wwwroot/uploads`). Điều này đảm bảo dự án có thể chạy và phát triển bình thường offline mà không bắt buộc có tài khoản Cloudinary.
- **Dịch vụ AI**: Nếu `Cloudflare:AccountId` hoặc `Cloudflare:ApiToken` bị thiếu, khi người dùng gọi tác vụ tạo ảnh, hệ thống thông báo lỗi cấu hình một cách tường minh mà không làm ảnh hưởng đến các thao tác soạn công thức thủ công.

---

## 3. Tích hợp lưu trữ đám mây Cloudinary

### 3.1 Lớp dịch vụ: `CloudinaryImageStorageService`
- Sử dụng gói NuGet chính thức `CloudinaryDotNet` (v1.29.3).
- Thực thi interface `IImageStorageService`:
  ```csharp
  Task<StoredImage> UploadAsync(Stream stream, ValidatedImageInfo info, CancellationToken cancellationToken = default);
  Task DeleteAsync(string providerPublicId, CancellationToken cancellationToken = default);
  ```
- Luôn kích hoạt cờ phân phối an toàn: `Api = { Secure = true }`.
- Tạo `ImageUploadParams` chỉ định `Folder = "recipe-card/steps"`, thiết lập `PublicId` duy nhất kết hợp GUID.
- Kết quả trả về gồm: `DeliveryUrl` (HTTPS), `ProviderPublicId`, `ByteSize`, `MimeType`, `OriginalFileName`.

### 3.2 Kiểm tra tính hợp lệ của tệp ảnh: `IImageValidator`
Mọi tệp ảnh (dù tải lên thủ công hay do AI tạo) đều phải đi qua `ImageValidator`:
- **Độ dài và dung lượng**: Tối đa 5 MB (5 * 1024 * 1024 bytes).
- **Phần mở rộng cho phép**: `.jpg`, `.jpeg`, `.png`, `.webp`.
- **Kiểm tra chữ ký tệp (Magic Bytes)**:
  - JPEG: `FF D8 FF`
  - PNG: `89 50 4E 47 0D 0A 1A 0A`
  - WEBP: `52 49 46 46` ... `57 45 42 50`
- **Chống tấn công Path Traversal**: Làm sạch tên tệp bằng `Path.GetFileName()`, ngăn ngừa ký tự độc hại (`../`, `..\\`).

### 3.3 Giao dịch đền bù (Compensating Transactions)
Khi lưu trữ tài nguyên số:
1. Hệ thống tải ảnh lên Cloudinary trước.
2. Lưu bản ghi `MediaAsset` vào cơ sở dữ liệu `RecipeDbContext`.
3. Nếu bước lưu CSDL phát sinh ngoại lệ, khối `catch` kích hoạt giao dịch đền bù: tự động gọi `_imageStorage.DeleteAsync(stored.ProviderPublicId)` để xóa ảnh vừa tải lên Cloudinary, ngăn ngừa hiện tượng rác tài nguyên (orphaned files).

### 3.4 Khả năng tự phục hồi khi xóa lỗi (`DeleteFailed`)
- Nếu xóa ảnh khỏi Cloudinary thất bại do lỗi mạng hoặc dịch vụ đám mây tạm thời không khả dụng:
  - Hệ thống không rollback bản ghi mà cập nhật `MediaAsset.State = MediaAssetState.DeleteFailed`.
  - Trên giao diện `/Media/Index`, ảnh hiển thị huy hiệu cảnh báo màu đen và cung cấp nút **"Thử lại xóa" (`RetryDelete`)**.
  - Khi người dùng bấm thử lại, hệ thống gọi lại lệnh hủy trên Cloudinary. Nếu thành công, bản ghi CSDL mới chính thức được xóa bỏ.

---

## 4. Tích hợp Cloudflare Workers AI & Quy trình ứng viên ảnh (Candidate Workflow)

### 4.1 Mô hình và Endpoint
- **Mô hình**: `@cf/black-forest-labs/flux-1-schnell` (Mô hình FLUX tốc độ cao tối ưu hóa cho đồ họa chân thực).
- **Endpoint**: `https://api.cloudflare.com/client/v4/accounts/{AccountId}/ai/run/@cf/black-forest-labs/flux-1-schnell`
- **Phương thức**: `POST`
- **Xác thực**: `Authorization: Bearer {ApiToken}`
- **Payload**: `{"prompt": "..."}`
- **Kết quả trả về**: Dữ liệu nhị phân ảnh (image bytes) kèm header `image/jpeg` hoặc `image/png`.

### 4.2 Lớp chuẩn hóa và dịch Prompt: `AiImagePromptBuilder` & `IAiPromptTranslator`
Để mô hình FLUX hiểu chính xác nội dung công thức tiếng Việt nhưng vẫn đảm bảo phong cách thương hiệu R&D:
1. **Tách nguồn tiếng Việt**: `AiImagePromptBuilder.BuildVietnameseSourcePrompt` tổng hợp tên công thức (`Recipe.Name`), thứ tự bước (`SortOrder`), nội dung hướng dẫn (`Instruction`), và ghi chú trực quan tùy chọn (`userBrief` giới hạn 500 ký tự) hoàn toàn bằng tiếng Việt.
2. **Dịch tự động qua Cloudflare**: `IAiPromptTranslator` (`CloudflareWorkersAiPromptTranslator`) gọi API mô hình `@cf/meta/m2m100-1.2b` (`POST .../run/@cf/meta/m2m100-1.2b`) với `{ text, source_lang: "vi", target_lang: "en" }` để chuyển ngữ cảnh sang tiếng Anh.
3. **Ghép phong cách nghệ thuật cố định**: `AiImagePromptBuilder.BuildFinalImagePrompt` nhận prompt tiếng Anh đã dịch và ghép thêm chỉ dẫn nhiếp ảnh thương mại chuẩn:
   - Phong cách: `Commercial beverage photography, professional barista tutorial aesthetic, clean studio lighting, realistic, high detail, Vietnamese tea and coffee style`.
   - Loại trừ: `No watermarks, no distorted objects, no artificial plastic look, no text overlays`.
4. **Bảo vệ an toàn**: Nếu dịch thất bại (lỗi mạng, quota, schema không khớp), hệ thống hủy ngay quy trình tạo ảnh và hiển thị thông báo lỗi rõ ràng, tuyệt đối không dùng prompt tiếng Việt làm fallback cho FLUX. Bản nháp lưu trữ chính xác `PromptSnapshot` tiếng Anh cuối cùng gửi sang FLUX.

### 4.3 Quản lý ứng viên tạm thời (Candidate Lifecycle)
Khác với việc tải trực tiếp vào kho dữ liệu chính, ảnh AI trải qua chu trình ứng viên:
```
[Bấm "Tạo ảnh AI"] 
        │
        ▼
[Cloudflare Workers AI] ───► [Lưu tạm App_Data/ai-drafts] ───► Trạng thái: Generated (Hạn 30 phút)
                                                                       │
                         ┌─────────────────────────────────────────────┼──────────────────────────────┐
                         ▼                                             ▼                              ▼
                 [Bấm "Chấp nhận"]                             [Bấm "Tạo lại"]                [Bấm "Bỏ qua"]
                         │                                             │                              │
                         ▼                                             ▼                              ▼
            1. Tải lên Cloudinary                         1. Xóa tệp tạm cũ               1. Đổi sang Discarded
            2. Tạo MediaAsset (AiIllustration)            2. Đổi sang Discarded           2. Xóa tệp tạm trên đĩa
            3. Gán MediaAssetId vào RecipeStep            3. Gọi AI tạo ứng viên mới
            4. Đổi draft sang Accepted
            5. Xóa tệp tạm trên đĩa
```

### 4.4 Ràng buộc nghiệp vụ ứng viên
1. **Duy nhất một ứng viên hoạt động**: Mỗi bước tại một thời điểm chỉ cho phép tồn tại tối đa một bản nháp ở trạng thái `Generated` còn hạn (`ExpiresUtc > DateTime.UtcNow`). Nếu đã có ứng viên, người dùng phải Chấp nhận hoặc Bỏ qua trước khi tạo mới.
2. **Thời hạn 30 phút**: Bản nháp lưu thông tin `ExpiresUtc = CreatedAtUtc.AddMinutes(30)`. Nếu quá 30 phút mà chưa được duyệt, bản nháp tự chuyển thành `Expired`.
3. **Tiến trình dọn dẹp nền (`AiDraftCleanupHostedService`)**:
   - Đăng ký dạng `IHostedService` trong `Program.cs`.
   - Chạy nền chu kỳ mỗi 10 phút.
   - Quét các bản nháp có `State == Expired || State == Discarded` hoặc `ExpiresUtc <= DateTime.UtcNow`.
   - Xóa tệp tương ứng trong `App_Data/ai-drafts` và cập nhật trạng thái trong CSDL.

---

## 5. Thư viện tài nguyên số tập trung (`/Media/Index`)

Màn hình Quản lý tài nguyên số được thiết kế tuân thủ nghiêm ngặt **Mobbin Design System** (`doc/DESIGN.md`):

### 5.1 Đặc tính giao diện (Mobbin Aesthetic)
- **Bảng màu**: Đơn sắc tối giản (Monochrome) — Mực đen near-black (`#141414`), nền canvas trắng (`#ffffff`), nền phụ soft canvas (`#f3f3f3`), viền hairline siêu mảnh 0.5px (`#e0e0e0`).
- **Typography**: Cấu trúc phông Saans với kích thước phân tầng rõ rệt: Display/Heading in đậm, text muted (`#707070`), nhãn nhỏ faint uppercase 11px với letter-spacing giãn nhẹ.
- **Thành phần điều khiển**: Nút bấm bo tròn dạng stadium-pill (`border-radius: 9999px`), thẻ card bo góc 24px (`var(--radius-lg)`), huy hiệu squircle bo tròn 30%.
- **Huy hiệu nhận diện nguồn**:
  - `Ảnh chụp`: Nền mềm nhạt `badge-pill-soft` (`#f3f3f3`).
  - `AI minh họa`: Nền đen tuyền `badge-pill-dark` (`#141414`, chữ trắng).

### 5.2 Bộ lọc phân loại (Filtering)
Hệ thống cung cấp các thẻ lọc linh hoạt kèm số lượng tổng hợp tự động cập nhật:
- **Tất cả (`all`)**: Toàn bộ tài nguyên số trong hệ thống.
- **Ảnh chụp (`real`)**: Các ảnh thực tế do nhân viên R&D tải lên (`SourceType == MediaSourceType.Real`).
- **Minh họa AI (`ai`)**: Các hình ảnh do Cloudflare Workers AI sinh và đã được duyệt (`SourceType == MediaSourceType.AiIllustration`).
- **Lỗi xóa (`failed`)**: Các ảnh đã phát lệnh xóa nhưng Cloudinary chưa xử lý thành công (`State == MediaAssetState.DeleteFailed`). Thẻ lọc này chỉ xuất hiện khi có ít nhất 1 ảnh lỗi.

### 5.3 Bảo vệ tham chiếu công thức (Reference Integrity Protection)
- Mỗi thẻ ảnh hiển thị danh sách các công thức đang sử dụng ảnh đó:
  - Nếu `ReferenceCount > 0`: Hiển thị huy hiệu dạng pill `Đang dùng trong X bước (Tên công thức...)` và **ẩn hoặc khóa nút xóa**.
  - Khi người dùng gửi yêu cầu xóa, máy chủ kiểm tra lại `asset.RecipeSteps.Count > 0`. Nếu đang được sử dụng, hệ thống từ chối xóa và hiển thị thông báo lỗi thân thiện:
    > *"Không thể xóa ảnh vì đang được sử dụng trong X bước công thức."*
- Chỉ cho phép xóa khi tài nguyên ảnh không còn bất kỳ bước công thức nào tham chiếu.

---

## 6. Công cụ CLI di chuyển dữ liệu ảnh bước quy trình cũ

Hệ thống cung cấp lệnh dòng lệnh tích hợp sẵn trong `Program.cs` để hỗ trợ di chuyển các dữ liệu ảnh cũ trước đây (lưu đường dẫn file trong cột `RecipeStep.ImageFileName`) sang mô hình tài nguyên số mới (`MediaAssetId`).

### 6.1 Cú pháp thực thi
```bash
# Từ thư mục gốc dự án:
dotnet run --project src/RecipeCard.Web -- --migrate-step-images --confirm
```

### 6.2 Cơ chế an toàn (Safety Guard)
- Nếu người dùng chỉ chạy:
  ```bash
  dotnet run --project src/RecipeCard.Web -- --migrate-step-images
  ```
- Hệ thống sẽ hiển thị cảnh báo màu vàng và dừng lại ngay lập tức mà không tác động vào CSDL:
  > `CẢNH BÁO: Cần cờ --confirm để tiến hành di chuyển ảnh bước thực hiện sang MediaAsset.`  
  > `Cú pháp: dotnet run -- --migrate-step-images --confirm`

### 6.3 Quy trình di chuyển từng bước
1. Khởi tạo Service Scope, lấy đối tượng `LegacyStepImageMigrationService`.
2. Truy vấn toàn bộ các dòng `RecipeStep` thỏa mãn điều kiện:
   ```csharp
   step.ImageFileName != null && step.MediaAssetId == null
   ```
3. Kiểm tra an toàn:
   - Làm sạch tên file bằng `Path.GetFileName` để ngăn chặn path traversal.
   - Kiểm tra file vật lý có tồn tại trong `wwwroot/uploads/steps/` hay không.
4. Đọc tệp và xác thực định dạng qua `IImageValidator` (kiểm tra dung lượng, định dạng và magic bytes).
5. Gọi `IImageStorageService.UploadAsync` (tải lên Cloudinary nếu có cấu hình, hoặc lưu vào storage chuẩn).
6. Khởi tạo `MediaAsset`:
   - `StorageProvider = "Cloudinary"` (hoặc Local)
   - `SourceType = MediaSourceType.Real`
   - `ProviderPublicId = stored.ProviderPublicId`
   - `DeliveryUrl = stored.DeliveryUrl`
7. Trong một giao dịch CSDL (`DbContext.SaveChangesAsync`):
   - Gán `step.MediaAsset = asset`
   - Gán `step.MediaAssetId = asset.Id`
   - Xóa bỏ dữ liệu cũ `step.ImageFileName = null`
8. Nếu gặp lỗi CSDL, kích hoạt cơ chế đền bù xóa file đã tải lên storage để tránh rác.

### 6.4 Mẫu báo cáo kiểm toán đầu ra
Sau khi hoàn tất, công cụ in ra báo cáo tổng kết chi tiết trên terminal:

```
=== BÁO CÁO DI CHUYỂN ẢNH CÔNG THỨC ===
Tổng số bước quét: 4
Thành công: 4
Thiếu file trên đĩa: 0
File không hợp lệ: 0
Lỗi tải lên nhà cung cấp: 0
Lỗi lưu CSDL: 0
 - Step #1: Đã chuyển đổi thành công tệp 'syphon-step1.jpg' sang MediaAsset #12
 - Step #2: Đã chuyển đổi thành công tệp 'syphon-step2.jpg' sang MediaAsset #13
 - Step #3: Đã chuyển đổi thành công tệp 'syphon-step3.jpg' sang MediaAsset #14
 - Step #4: Đã chuyển đổi thành công tệp 'syphon-step4.jpg' sang MediaAsset #15
```

---

## 7. Hướng dẫn kiểm thử và xác minh tính năng (Verification)

### 7.1 Bộ kiểm thử tự động (Unit & Integration Tests)
Toàn bộ hệ thống được bảo vệ bởi bộ kiểm thử tự động với 50 bài test chạy hoàn toàn độc lập và không phụ thuộc vào kết nối mạng:
```bash
dotnet test
```
*Kết quả yêu cầu*: **75 passed, 0 failed, 0 skipped**.

### 7.2 Các kịch bản kiểm thử trọng yếu
1. **Kiểm tra Mock Cloudinary & Fallback**:
   - `CloudinaryImageStorageServiceTests`: Kiểm thử xác thực cấu hình `CloudinaryOptions`, upload tệp, xóa tệp, kích hoạt đền bù.
2. **Kiểm tra Mock Cloudflare Workers AI**:
   - `CloudflareWorkersAiPromptTranslatorTests`: Kiểm thử dịch prompt Việt -> Anh với các envelope `result.translated_text` / `result[0].translated_text`, xử lý lỗi API Cloudflare, lỗi mạng và che giấu token an toàn.
   - `CloudflareWorkersAiImageGeneratorTests`: Kiểm thử tạo ảnh thành công, xử lý lỗi mạng, xử lý lỗi xác thực token, kiểm tra timeout 60s.
3. **Kiểm thử chu trình ứng viên AI**:
   - Tạo nháp (`OnPostGenerateAiImageAsync`) -> Xem trước -> Tạo lại (`OnPostRegenerateAiImageAsync`) -> Chấp nhận (`OnPostAcceptAiImageAsync`) -> Kiểm tra chuyển đổi thành `MediaAsset` với nguồn `AiIllustration`.
4. **Kiểm thử Thư viện ảnh `/Media/Index`**:
   - Tải ảnh hợp lệ -> Kiểm tra bộ lọc `real`/`ai`/`failed`.
   - Kiểm tra chặn xóa ảnh đang được tham chiếu bởi công thức.
   - Kiểm tra chuyển trạng thái `DeleteFailed` và nút `RetryDelete`.
5. **Kiểm thử CLI Tool Migration**:
   - Kiểm thử chạy thiếu `--confirm` bị chặn lại an toàn.
   - Kiểm thử quét và di chuyển thành công ảnh bước cũ vào `MediaAsset`.

---

## 8. Hướng dẫn khắc phục sự cố thường gặp (Troubleshooting)

| Tình huống sự cố | Nguyên nhân có thể | Hướng xử lý |
| :--- | :--- | :--- |
| **Không tải được ảnh lên Cloudinary** | Thiếu thông tin `CloudName`, `ApiKey`, `ApiSecret` hoặc sai khóa bí mật | Kiểm tra cấu hình trong `appsettings.json` hoặc biến môi trường; nếu đang chạy local offline, xóa `Cloudinary:CloudName` để hệ thống tự động fallback về lưu trữ local. |
| **Lỗi khi dịch prompt AI ("Dịch mô tả cho AI thất bại...")** | Lỗi kết nối Cloudflare Workers AI, tài khoản hết quota hoặc model dịch gặp sự cố | Kiểm tra `ApiToken` và `AccountId`, kiểm tra trạng thái dịch vụ Workers AI của Cloudflare. |
| **Lỗi khi gọi tạo ảnh AI ("Dịch vụ AI không thể xử lý...")** | `AccountId` hoặc `ApiToken` Cloudflare không chính xác, hoặc tài khoản Cloudflare bị giới hạn hạn mức (Rate limit/Quota) | Kiểm tra `ApiToken` phải có quyền `Workers AI Read`. Xem chi tiết thông báo lỗi trong log ứng dụng. |
| **Không thể xóa ảnh trong Thư viện Media** | Ảnh đang được liên kết trong ít nhất một bước công thức | Kiểm tra danh sách công thức đang sử dụng hiển thị trên thẻ ảnh; xóa hoặc thay thế ảnh trong các bước công thức đó trước khi xóa khỏi thư viện. |
| **Thẻ ảnh hiển thị trạng thái "Lỗi xóa"** | Mạng gián đoạn trong lúc gọi Cloudinary Destroy API | Đảm bảo kết nối Internet ổn định và bấm nút **"Thử lại xóa"** trên thẻ ảnh để hoàn tất việc dọn dẹp. |
