# Phase 5: Kiểm Thử Tự Động & Tài Liệu Vận Hành

## 1. Mục tiêu
- Viết test suite toàn diện kiểm thử cấu trúc prompt, tính trung thành của bản dịch, multipart generator, vòng đời draft và tích hợp PDF.
- Đảm bảo toàn bộ regression tests (Gemini round-robin 15 lượt 429, Llama fallback, cancellation, timeout) tiếp tục pass 100%.
- Cập nhật tài liệu kỹ thuật `docs/materials/MEDIA_AND_AI_OPERATIONS.md`.

## 2. Thay đổi chi tiết

### 2.1. Unit & Contract Tests trong RecipeCard.Web.Tests
- **`AiImagePromptBuilderTests.cs`**:
  - `BuildStepSourcePrompt`: kiểm tra chứa đủ tên công thức, mọi nguyên liệu kèm số lượng, các bước trước theo đúng thứ tự; không chứa bất kỳ bước nào trong tương lai.
  - `BuildStepSourcePrompt`: kiểm tra giữ nguyên toàn bộ hành động (ví dụ "rót rồi khuấy nhẹ").
  - `BuildFinalProductSourcePrompt`: kiểm tra chứa toàn bộ nguyên liệu và toàn bộ quy trình; không chứa nhãn CURRENT STEP.
  - `AttachCanonicalStyle`: kiểm tra gắn đúng khối quy tắc của `StepInstruction` hoặc `FinalProduct`.
  - `AttachCanonicalStyle`: kiểm tra chặn quăng lỗi khi prompt vượt quá 8.000 ký tự.
- **`AiPromptTranslatorFaithfulTests.cs`**:
  - Kiểm tra cả `GeminiRoundRobinPromptTranslator` và `CloudflareWorkersAiPromptTranslator` đều gửi cùng một chuỗi `FaithfulSystemInstruction`.
  - Kiểm tra Gemini request có `maxOutputTokens = 4096`.
  - Kiểm tra bản dịch thiếu nhãn section (ví dụ mất `TARGET:` hoặc `RECIPE:`) bị bắt lỗi và không cho đi tiếp.
- **`CloudflareWorkersAiImageGeneratorTests.cs`**:
  - Kiểm tra request gửi đi dạng `MultipartFormDataContent` tới `@cf/black-forest-labs/flux-2-klein-4b`.
  - Kiểm tra 4 preset (`Square1x1`, `StandardLandscape4x3`, `WideLandscape16x9`, `Portrait4x5`) truyền đúng cặp `width` và `height`.
  - Kiểm tra request không chứa trường `steps`.
  - Kiểm tra giải mã base64 response thành công và ném lỗi nếu chuỗi base64 hỏng.
  - Kiểm tra không log nội dung prompt hay base64 string.
- **`RecipeFinalImageLifecycleTests.cs`**:
  - Kiểm tra tạo nháp AI ảnh thành phẩm gán đúng `TargetKind = FinalProduct` và `RecipeId`.
  - Kiểm tra chấp nhận draft thành phẩm gán vào `Recipe.FinalMediaAssetId`, không chạm vào `RecipeStep`.
  - Kiểm tra gán ảnh từ upload hoặc chọn media library cho ảnh thành phẩm.
- **`RecipePdfHeroImageTests.cs`**:
  - Kiểm tra khi recipe có ảnh thành phẩm, PDF sinh ra chứa dữ liệu hình ảnh hero ở đầu trang.
  - Kiểm tra khi recipe chưa có ảnh thành phẩm, PDF vẫn render sạch sẽ không bị vỡ layout.

### 2.2. Cập nhật Tài liệu Vận hành
- **`docs/materials/MEDIA_AND_AI_OPERATIONS.md`**:
  - Cập nhật sơ đồ kiến trúc mới: Dữ liệu có cấu trúc → Dịch trung thành → Server Canonical Style → FLUX.2 Klein 4B.
  - Bổ sung bảng 4 preset tỷ lệ kích thước (1:1, 4:3, 16:9, 4:5).
  - Bổ sung tài liệu về vòng đời ảnh thành phẩm công thức (Upload, Media Library, AI Draft).
  - Ghi chú về quota Cloudflare Workers AI: 10.000 Neurons miễn phí mỗi ngày, FLUX.2 Klein 4B tiêu tốn ~26.05 Neurons / tile 512×512.
  - Ghi chú về an toàn logging: không in prompt, không in response raw, tắt SQL queries debug.

## 3. Tiêu chí nghiệm thu Phase 5
- Chạy `dotnet test tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj`: 100% tests passed.
- Tài liệu `MEDIA_AND_AI_OPERATIONS.md` được cập nhật đồng bộ, không còn tham chiếu cũ đến `flux-1-schnell` hay `M2M100`.
