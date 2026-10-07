# Nhật ký thực hiện: Triển khai Phase 1 - Shared PDF Model và Media Loader

- **Thời gian**: 2026-10-07
- **Kế hoạch**: `docs/plans/261007-selected-recipe-booklet/plan.md`
- **Trạng thái kế hoạch**: `blocked` (Phase 1 hoàn thành, Phase 2-4 bị chặn do thiếu asset logo)

## 1. Mục tiêu và phạm vi thực hiện

Triển khai Phase 1 của kế hoạch xuất booklet:
- Tách tầng mapping và tải media khỏi `PreviewModel` sang các service dùng chung (`PdfMediaLoader`, `RecipePdfModelFactory`).
- Thiết lập pipeline nén/chuẩn hóa ảnh an toàn với ImageSharp 4.1.2: giới hạn concurrency (4 remote, 2 decode), giới hạn input (8 MiB, 16 MP, 6000 px), giới hạn output (JPEG <= 750 KiB, ngân sách batch 64 MiB và 75 MP).
- Bảo đảm quản lý vòng đời file tạm trong `%TEMP%` thông qua `RecipePdfModelBatch: IAsyncDisposable`.
- Đảm bảo hợp đồng xuất PDF đơn qua Preview giữ nguyên, không hồi quy.

## 2. File thay đổi (chỉ trong phạm vi Phase 1)

- `src/RecipeCard.Web/RecipeCard.Web.csproj` (thêm SixLabors.ImageSharp 4.1.2)
- `src/RecipeCard.Web/Program.cs` (đăng ký `PdfMediaLoader`, `IRecipePdfModelFactory`, cấu hình client `MediaDelivery` timeout 10s)
- `src/RecipeCard.Web/Services/PdfMediaLoader.cs` (mới)
- `src/RecipeCard.Web/Services/RecipePdfModelFactory.cs` (mới)
- `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs` (refactor dùng factory)
- `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` (cập nhật constructor)
- `tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` (mới - 10 behavioral tests)
- `docs/plans/261007-selected-recipe-booklet/plan.md` (cập nhật trạng thái Phase 1 completed, toàn plan blocked)
- `docs/plans/261007-selected-recipe-booklet/phase-01-shared-pdf-model-and-media.md` (cập nhật checklist)

*(Lưu ý: Giữ nguyên toàn bộ các thay đổi ambient/unrelated khác trong working directory như Edit.*, AiPrompt*, RULE.md).*

## 3. Kết quả kiểm thử

- `dotnet test tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj`
- Kết quả: **134/134 test passed** (bao gồm 10 tests mới đo concurrency semaphores, limits, frame count, fallback và directory cleanup).

## 4. Lý do dừng ở Phase 1

- Phase 2 và Phase 3 yêu cầu file SVG logo thương hiệu chính thức tại `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg`.
- Do asset này chưa được cung cấp và quy chuẩn cấm tạo logo placeholder/fake, tiến độ thực hiện được dừng lại đúng ranh giới Phase 1 và đánh dấu `blocked` theo quy định.
