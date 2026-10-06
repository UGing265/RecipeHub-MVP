# Phase 4: Ảnh Thành Phẩm, Vòng Đời Draft & Tích Hợp UI/PDF

## 1. Mục tiêu
- Xây dựng luồng tạo ảnh thành phẩm (Hero Product Image) cho Recipe hỗ trợ cả 3 nguồn: Upload file, Thư viện media, AI Draft.
- Nâng cấp luồng draft: hỗ trợ cả `StepInstruction` và `FinalProduct`, ngăn chặn việc nhầm lẫn draft giữa step và recipe.
- Cho phép chọn preset tỷ lệ (1:1, 4:3, 16:9, 4:5) khi tạo ảnh AI.
- Tích hợp hiển thị ảnh thành phẩm ở 4 bề mặt: Trang chỉnh sửa (`Edit.cshtml`), Danh sách (`Index.cshtml`), Xem trước (`Preview.cshtml`), và Xuất PDF (`RecipePdfDocument.cs`).

## 2. Thay đổi chi tiết

### 2.1. Handlers trong Edit.cshtml.cs
- **Tạo ảnh bước AI (`OnPostGenerateAiImageAsync`, `OnPostRegenerateAiImageAsync`)**:
  - Đọc toàn bộ nguyên liệu của công thức.
  - Đọc các bước có `SortOrder < currentStep.SortOrder` sắp xếp tăng dần.
  - Đọc tham số `AspectRatioPreset` từ form gửi lên (mặc định `Square1x1`).
  - Dựng prompt bước qua `AiImagePromptBuilder.BuildStepSourcePrompt`.
  - Dịch qua `IAiPromptTranslator`.
  - Ghép canonical style bước qua `AiImagePromptBuilder.AttachCanonicalStyle`.
  - Gọi `IAiImageGenerator.GenerateImageBytesAsync` với preset đã chọn.
  - Lưu draft với `TargetKind = AiDraftTargetKind.StepInstruction`, `RecipeId`, `RecipeStepId`.
- **Tạo ảnh thành phẩm AI (`OnPostGenerateFinalAiImageAsync`, `OnPostRegenerateFinalAiImageAsync`)**:
  - Đọc toàn bộ nguyên liệu và toàn bộ quy trình các bước.
  - Đọc tham số `AspectRatioPreset` từ form gửi lên (mặc định `WideLandscape16x9`).
  - Dựng prompt qua `AiImagePromptBuilder.BuildFinalProductSourcePrompt`.
  - Dịch qua `IAiPromptTranslator`.
  - Ghép canonical style thành phẩm qua `AiImagePromptBuilder.AttachCanonicalStyle`.
  - Gọi `IAiImageGenerator.GenerateImageBytesAsync`.
  - Lưu draft với `TargetKind = AiDraftTargetKind.FinalProduct`, `RecipeId`, `RecipeStepId = null`.
- **Chấp nhận / Hủy draft (`OnPostAcceptAiDraftAsync`, `OnPostDiscardAiDraftAsync`)**:
  - Kiểm tra `draft.TargetKind`:
    - Nếu `StepInstruction`: gán `MediaAsset` vào `step.MediaAssetId`, cập nhật `step.ImageAspectRatioPreset`.
    - Nếu `FinalProduct`: gán `MediaAsset` vào `recipe.FinalMediaAssetId`, cập nhật `recipe.FinalImageAspectRatioPreset`.
  - Đảm bảo tính nguyên tử (Transaction).
- **Upload / Chọn thư viện cho ảnh thành phẩm**:
  - `OnPostUploadFinalImageAsync`: upload file ảnh kiểm tra magic bytes, lưu media asset và gán `recipe.FinalMediaAssetId`.
  - `OnPostSelectFinalImageFromLibraryAsync`: chọn asset có sẵn và gán vào `recipe.FinalMediaAssetId`.
  - `OnPostRemoveFinalImageAsync`: hủy liên kết `recipe.FinalMediaAssetId = null` (không xóa file asset nếu đang dùng chung).

### 2.2. Giao diện Razor Pages
- **`src/RecipeCard.Web/Pages/Recipes/Edit.cshtml`**:
  - Bổ sung khối "Ảnh đại diện thành phẩm" ở đầu trang (dưới tiêu đề công thức):
    - Hiển thị ảnh hiện tại (nếu có) kèm tỷ lệ khung hình.
    - Nút tải ảnh lên từ máy.
    - Nút chọn từ thư viện media.
    - Nút "Tạo ảnh đại diện AI" mở modal/khung nhập: chọn preset tỷ lệ (1:1, 4:3, 16:9, 4:5), ô yêu cầu thêm (tối đa 500 ký tự), nút "Tạo ảnh nháp".
    - Khung xem trước bản nháp AI thành phẩm kèm nút "Chấp nhận" và "Tạo lại / Bỏ qua".
  - Trong từng bước thực hiện:
    - Bổ sung dropdown chọn preset tỷ lệ khi bấm tạo ảnh AI (mặc định Vuông 1:1).
- **`src/RecipeCard.Web/Pages/Recipes/Index.cshtml`**:
  - Hiển thị thumbnail ảnh thành phẩm của công thức ở từng thẻ card. Nếu chưa có ảnh thành phẩm, hiển thị placeholder trung tính hoặc icon món.
- **`src/RecipeCard.Web/Pages/Recipes/Preview.cshtml`**:
  - Hiển thị Hero banner ảnh thành phẩm ở đầu trang trước bảng nguyên liệu và các bước.

### 2.3. Tích hợp Xuất PDF (QuestPDF)
- **`src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs`**:
  - Truy vấn `Recipe.FinalMediaAsset` và nạp mảng byte vào `RecipePdfModel.HeroImageBytes` và `HeroImageMimeType`.
- **`src/RecipeCard.Web/Services/RecipePdfDocument.cs`**:
  - Kiểm tra `model.HeroImageBytes != null`:
    - Hiển thị khối hero image ở đầu trang tài liệu PDF với khung viền hairline và bo góc chuẩn hệ thống thiết kế Monochrome.
    - Sử dụng cơ chế scale vừa khung theo preset tỷ lệ, không làm méo hoặc cắt mất nội dung chính.

## 3. Tiêu chí nghiệm thu Phase 4
- Thao tác thực tế trên web:
  - Có thể upload ảnh thành phẩm, chọn từ media library hoặc bấm tạo AI thành phẩm.
  - Bấm tạo ảnh AI bước: chọn được tỷ lệ (ngang/vuông/dọc) và ảnh sinh ra đúng kích thước đã chọn.
  - Chấp nhận draft thành phẩm: hiển thị ngay ở Edit, sang Index thấy thumbnail, sang Preview thấy hero banner.
  - Bấm "Xuất file PDF": file PDF tải về có ảnh hero ở đầu trang công thức.
