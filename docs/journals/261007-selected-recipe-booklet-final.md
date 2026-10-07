# Nhật ký kỹ thuật: Hoàn thành toàn bộ Kế hoạch Xuất PDF Booklet Công Thức Đã Chọn (Phases 1–4)

- **Ngày thực hiện**: 2026-10-07
- **Kế hoạch**: `docs/plans/261007-selected-recipe-booklet/plan.md`
- **Trạng thái**: Hoàn thành (completed)

## 1. Tóm tắt kết quả triển khai

Đã hoàn thiện toàn diện tính năng xuất tài liệu booklet từ 1 đến 50 công thức theo đúng tài liệu thiết kế (`DESIGN.md` và `plan.md`):

1. **Phase 1: Pipeline xử lý media & Shared factory**
   - Tích hợp `SixLabors.ImageSharp` 3.1.12 (bản vá an toàn lỗ hổng bảo mật NU1902).
   - Thiết lập `PdfMediaLoader`: quản lý giới hạn tài nguyên nghiêm ngặt (4 luồng fetch, 2 luồng giải mã/resize song song, nén JPEG $\le$ 750 KiB, ngân sách batch 64 MiB và 75 MP).
   - Triển khai `RecipePdfModelFactory` và `RecipePdfModelBatch: IAsyncDisposable` dọn dẹp sạch sẽ tệp tạm trong `%TEMP%/recipe-card-pdf/{guid}`.
   - Refactor `Preview.cshtml.cs` sang dùng chung factory, giữ nguyên hoàn toàn hợp đồng xuất PDF đơn lẻ.

2. **Phase 2: Thiết kế Booklet Document & Tích hợp Vector Brand**
   - Đưa vector SVG thương hiệu chính thức từ `docs/materials/logo-phe-la/logo-phe-la.svg` vào đường dẫn xuất bản: `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg`.
   - Xây dựng `RecipeBookletPdfDocument`:
     - Trang bìa (Trang 1): 58% cột trái nền Ink đen chữ trắng mang thông điệp đào tạo, 42% cột phải nền Canvas trắng chứa logo SVG tỉ lệ gốc sắc nét.
     - Trang Mục lục (TOC): tự động phân trang theo chunk chuẩn (15 công thức/trang), đảm bảo không bao giờ tràn trang khi xuất đủ 50 công thức có tiêu đề dài.
     - Các trang chi tiết: tách thành component dùng chung `RecipePdfPageComposer`, tôn trọng phong cách Mobbin monochrome.

3. **Phase 3: Giao diện chọn công thức & Hợp đồng POST All-or-Nothing**
   - Cập nhật `/Recipes/Index`:
     - Bổ sung thanh công cụ `booklet-toolbar` với counter trực quan `Đã chọn X/50` và nút bấm xuất PDF.
     - Checkbox chọn từng mục và chọn tất cả ở tiêu đề bảng (tự giới hạn tối đa 50 công thức sẵn sàng đầu tiên theo `Id DESC`).
     - Các công thức chưa đủ điều kiện tự động bị disabled checkbox.
     - Thiết kế chuẩn Monochrome theo `DESIGN.md`, không dùng màu sặc sỡ.
   - Xử lý POST `OnPostExportSelectedAsync`:
     - Kiểm tra bắt buộc token Antiforgery (`__RequestVerificationToken`).
     - Kiểm tra chặt chẽ quy tắc toàn-hoặc-không (all-or-nothing): rỗng, >50, trùng lặp, ID âm/sai định dạng, ID không tồn tại, công thức chưa sẵn sàng đều trả về HTTP 400 và hiển thị thông báo lỗi chi tiết.
     - Tên tệp xuất bản chuẩn: `recipe-collection-yyyyMMdd-HHmm.pdf`.

4. **Phase 4: Kiểm thử tích hợp HTTP, Antiforgery & Tài liệu vận hành**
   - Xây dựng bộ test tích hợp `RecipeIndexAntiforgeryTests` bằng `WebApplicationFactory<Program>`, chứng minh server thật chặn request thiếu token (HTTP 400) và xuất file PDF hợp lệ khi có token hợp lệ (HTTP 200).
   - Bổ sung `SelectedRecipeBookletExportTests` kiểm thử đầy đủ các điều kiện biên của booklet, metadata và thứ tự `Id DESC`.
   - Cập nhật tài liệu vận hành chi tiết tại `docs/materials/MEDIA_AND_AI_OPERATIONS.md`.

## 2. Kết quả kiểm thử cuối cùng

- `dotnet build src/RecipeCard.Web/RecipeCard.Web.csproj --configuration Release` $\rightarrow$ **0 Warning, 0 Error**.
- `dotnet test tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj` $\rightarrow$ **151/151 test PASSED** (0 failed, 0 skipped).
