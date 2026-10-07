---
title: "Xuất PDF tổng hợp công thức đã chọn"
description: "Xuất một booklet QuestPDF từ 1–50 công thức sẵn sàng, có bìa, mục lục, media được chuẩn hóa và giữ nguyên luồng xuất đơn."
status: completed
priority: P1
created: 2026-10-07
effort: 16h
branch: main
design: ./design.md
blockedBy: []
blocks: []
tags: [feature, razor-pages, questpdf, pdf, media]
---

# Kế hoạch triển khai PDF tổng hợp công thức đã chọn

> **Dành cho agent triển khai:** dùng `executing-plans` hoặc `subagent-driven-development`; thực hiện tuần tự từng phase, theo checklist trong phase.

## 1. Mục tiêu và nguồn chuẩn

- Nguồn quyết định: [design.md](./design.md).
- Từ `Recipes/Index`, người dùng chọn 1–50 công thức sẵn sàng và nhận đúng một file PDF.
- Booklet gồm bìa A4 ngang, mục lục bắt đầu ở trang 2 và có thể tự phân trang, rồi từng công thức bắt đầu ở trang mới sau mục lục.
- Thứ tự do server quyết định: `Recipe.Id DESC`, không tin thứ tự hoặc trạng thái checkbox từ client.
- Luồng `GET /Recipes/Preview/{id}?handler=Pdf` giữ nguyên URL, điều kiện sẵn sàng, loại kết quả, tên file, bố cục/nội dung recipe và fallback ảnh; phần mapping/media được chuyển sang dịch vụ dùng chung, không sao chép vào `IndexModel`.
- Cover, TOC, recipe composer dùng chung và UI chọn mới chuyển về đen, trắng, xám theo `docs/materials/DESIGN.md`; không thêm màu Bootstrap, bóng đổ hoặc accent ngoài monochrome.

## 2. Kiến trúc chốt

### 2.1. Luồng dữ liệu

```text
GET /Recipes
  -> IndexModel.LoadRecipesAsync()
  -> projection theo Id DESC
  -> checkbox chỉ bật khi IngredientCount > 0 && StepCount > 0

POST /Recipes?handler=ExportSelected + antiforgery + SelectedRecipeIds[]
  -> kiểm tra model binding, rỗng, > 50, ID trùng
  -> một truy vấn AsNoTracking + AsSplitQuery + Include toàn bộ graph theo các ID
  -> tránh cartesian explosion giữa Ingredients và Steps; đối chiếu ID thiếu và kiểm tra lại trạng thái sẵn sàng
  -> OrderByDescending(Id) ở server
  -> IRecipePdfModelFactory.CreateAsync(recipes, RequestAborted)
       -> PdfMediaLoader tải/đọc, kiểm tra, resize và ghi file tạm có giới hạn
       -> RecipePdfModelBatch (models cùng thứ tự + vòng đời file tạm)
  -> RecipeBookletPdfDocument.GeneratePdf()
  -> application/pdf; recipe-collection-{yyyyMMdd-HHmm}.pdf
  -> dispose batch trong finally để xóa file tạm

GET /Recipes/Preview/{id}?handler=Pdf
  -> truy vấn một recipe như hiện tại
  -> cùng IRecipePdfModelFactory.CreateAsync([recipe], RequestAborted)
  -> RecipePdfDocument dùng component trang công thức chung
  -> giữ response/tên file hiện tại
```

### 2.2. Ranh giới trách nhiệm

- `RecipePdfModelFactory`: chỉ map entity graph đã tải sang `RecipePdfModel`; không truy vấn database, không quyết định batch hợp lệ.
- `PdfMediaLoader`: tải/đọc và chuẩn hóa ảnh; giới hạn tài nguyên, timeout, cancellation, file tạm, fallback.
- `RecipePdfPageComposer`: component QuestPDF duy nhất cho nội dung một công thức; cả PDF đơn và booklet gọi lại.
- `RecipeBookletPdfDocument`: chỉ ghép cover, TOC và các section công thức.
- `IndexModel`: authority cho validation batch và truy vấn; không chứa code tải ảnh hoặc mapping PDF.

### 2.3. Giới hạn media bắt buộc

| Ràng buộc | Giá trị |
|---|---:|
| Số request remote đồng thời | 4 |
| Số decode/resize đồng thời | 2 |
| Timeout mỗi media remote | 10 giây |
| Deadline chuẩn bị media toàn batch | 60 giây; media còn chờ bị bỏ qua, PDF vẫn sinh |
| Kích thước input nén tối đa | 8 MiB/ảnh; kiểm tra `Content-Length` và stream đếm byte |
| Kích thước decoded đầu vào | tối đa 6.000 px mỗi chiều và 16 megapixel cho frame đầu |
| Frame decode | chỉ frame đầu, bỏ metadata; không decode ảnh động nhiều frame |
| Hero sau resize | tối đa 1.200 × 750, không upscale |
| Ảnh bước sau resize | tối đa 900 × 600, không upscale |
| Output chuẩn hóa | JPEG quality 82, tối đa 750 KiB/ảnh |
| Ngân sách toàn batch | tối đa 64 MiB encoded và 75 megapixel decoded của output |

Dùng `SixLabors.ImageSharp` 4.1.2 để `Identify` trước decode, chỉ decode frame đầu với metadata bị bỏ, resize bằng `ResizeMode.Max`, nền trắng cho ảnh có alpha, encode JPEG. Nếu output vượt 750 KiB, giảm quality theo các mức 72/62 rồi giảm kích thước 85% cho tới ngưỡng; dưới cạnh dài 480 px mà vẫn vượt ngưỡng thì coi ảnh không khả dụng. Cấp ngân sách theo thứ tự đã chốt: recipe `Id DESC`, hero trước, ảnh bước theo `SortOrder`; chạy theo cửa sổ nhỏ để concurrency không làm thay đổi ảnh nào được giữ.

## 3. Hợp đồng validation POST

Handler: `OnPostExportSelectedAsync(CancellationToken cancellationToken)` với `[BindProperty] List<int> SelectedRecipeIds`.

Mọi lỗi dưới đây trả lại trang Index với HTTP `400`, tải lại danh sách `Id DESC`, giữ checked cho ID hợp lệ còn hiển thị, thêm đúng một lỗi vào validation summary và **không sinh PDF**:

| Thứ tự | Điều kiện | Thông báo chính xác |
|---:|---|---|
| 1 | Model binding lỗi/ID không phải số nguyên dương | `Dữ liệu lựa chọn công thức không hợp lệ.` |
| 2 | Danh sách rỗng | `Hãy chọn ít nhất 1 công thức để xuất PDF.` |
| 3 | Có hơn 50 phần tử | `Chỉ có thể xuất tối đa 50 công thức mỗi lần.` |
| 4 | Có ID trùng | `Danh sách công thức có ID trùng lặp. Vui lòng chọn lại.` |
| 5 | Có ID không tồn tại | `Không tìm thấy công thức đã chọn: {danh sách ID giảm dần}.` |
| 6 | Có recipe không sẵn sàng | `Công thức chưa đủ điều kiện xuất PDF: {Tên} (#{Id}), ...` |

Form Razor dùng `method="post" asp-page-handler="ExportSelected"`, do đó antiforgery token được sinh và kiểm tra tự động. Không thêm `[IgnoreAntiforgeryToken]`. Checkbox có cùng tên `SelectedRecipeIds`; checkbox header không có `name`. Server không âm thầm lọc, deduplicate hoặc sửa thứ tự input. Sau khi toàn bộ batch hợp lệ, server luôn sắp `Id DESC` trước mapping, TOC và nội dung.

## 4. Cấu trúc booklet và logo

- Tất cả trang A4 ngang.
- Trang 1: composition theo trang 1 của `docs/materials/Bộ công thức Phê La Update 13_07_2026.pdf`: mảng cọ đen lớn bên trái, tiêu đề trắng `BỘ HƯỚNG DẪN\nPHA CHẾ SẢN PHẨM`; logo Phê La lớn bên phải; góc trái dưới có `Phòng đào tạo - Phê La` và `Xuất bởi R&D Recipe Hub • dd/MM/yyyy • N công thức`.
- Mục lục bắt đầu ở trang 2, giữ thứ tự recipe `Id DESC` và tự chảy sang thêm trang khi tên dài hoặc đủ 50 mục.
- Sau trang cuối của mục lục: mỗi recipe mở một page section mới và dùng `RecipePdfPageComposer`; recipe dài có thể tràn sang trang tiếp theo nhưng recipe kế tiếp luôn bắt đầu trang mới.
- Logo chỉ xuất hiện trên bìa. Asset bắt buộc: `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg`. Đây phải là SVG chính thức, có quyền sử dụng, nền trong suốt và đã kiểm tra ở kích thước in; không crop từ PDF mẫu, không dựng logo giả, không fallback chữ. Thiếu hoặc không đọc được asset phải dừng export với lỗi cấu hình rõ ràng và được bắt ở smoke trước phát hành.
- Metadata file: `Title = "Bộ hướng dẫn pha chế sản phẩm"`, `Author = "R&D Recipe Hub"`, `Creator = "R&D Recipe Hub"`, `Subject = $"{N} công thức"`, `CreationDate` từ `TimeProvider`.

## 5. Các phase

| Phase | Nội dung | Trạng thái |
|---|---|---|
| [Phase 1](./phase-01-shared-pdf-model-and-media.md) | Trích xuất mapping/media dùng chung, giới hạn tài nguyên, chuyển Preview sang dịch vụ | completed |
| [Phase 2](./phase-02-booklet-document-and-brand.md) | Component recipe chung, cover/TOC/booklet, metadata và logo chính thức | completed |
| [Phase 3](./phase-03-index-selection-and-post-contract.md) | Checkbox UX, POST antiforgery, validation toàn-or-nothing và `Id DESC` | completed |
| [Phase 4](./phase-04-regression-smoke-and-docs.md) | Regression/integration tests, smoke app 50 recipe, tài liệu vận hành | completed |

Phase sau chỉ bắt đầu khi tiêu chí phase trước đạt; chạy build/test tập trung ở Phase 4 để tránh kiểm tra lặp.

## 6. Danh sách file triển khai

Project root tuyệt đối: `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi`.

| Thao tác | Đường dẫn tương đối | Đường dẫn tuyệt đối |
|---|---|---|
| Modify | `src/RecipeCard.Web/RecipeCard.Web.csproj` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/RecipeCard.Web.csproj` |
| Modify | `src/RecipeCard.Web/Program.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Program.cs` |
| Create | `src/RecipeCard.Web/Services/RecipePdfModelFactory.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Services/RecipePdfModelFactory.cs` |
| Create | `src/RecipeCard.Web/Services/PdfMediaLoader.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Services/PdfMediaLoader.cs` |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pages/Recipes/Preview.cshtml.cs` |
| Modify | `src/RecipeCard.Web/Pdf/RecipePdfDocument.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pdf/RecipePdfDocument.cs` |
| Create | `src/RecipeCard.Web/Pdf/RecipePdfPageComposer.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pdf/RecipePdfPageComposer.cs` |
| Create | `src/RecipeCard.Web/Pdf/RecipeBookletPdfDocument.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pdf/RecipeBookletPdfDocument.cs` |
| Create/dependency | `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg` |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Index.cshtml.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pages/Recipes/Index.cshtml.cs` |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Index.cshtml` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pages/Recipes/Index.cshtml` |
| Modify | `src/RecipeCard.Web/wwwroot/css/site.css` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/wwwroot/css/site.css` |
| Modify | `tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj` |
| Modify | `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/RecipeIndexAntiforgeryTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipeIndexAntiforgeryTests.cs` |
| Modify | `docs/materials/MEDIA_AND_AI_OPERATIONS.md` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/docs/materials/MEDIA_AND_AI_OPERATIONS.md` |

Không cần migration/schema database.

## 7. Quan hệ với kế hoạch khác

`docs/plans/261006-2350-unified-prompt-and-final-product-image/` đã dự kiến hero image trên PDF; code hero/media tương ứng đã có trong baseline. Kế hoạch này phụ thuộc vào baseline đó để tái sử dụng `FinalMediaAsset` và `MediaAsset`, nhưng không bị block và không sửa frontmatter kế hoạch cũ.


Logo SVG chính thức là dependency ngoài code: không bắt đầu Phase 2/3 integration trước khi asset hợp lệ được cung cấp. Đây không phải quan hệ `blockedBy` với một plan khác nên frontmatter vẫn để `blockedBy: []`.
## 8. Tiêu chí hoàn thành tổng thể

1. Chọn 1–50 recipe sẵn sàng từ Index và tải đúng một `application/pdf` tên `recipe-collection-{yyyyMMdd-HHmm}.pdf`.
2. Client không chọn được recipe chưa sẵn sàng hoặc recipe thứ 51; server vẫn từ chối chính xác mọi request rỗng, >50, trùng, thiếu hoặc không sẵn sàng với HTTP 400 và không xuất một phần.
3. Cover, TOC và content cùng thứ tự `Id DESC`; TOC được phép phân trang; mỗi recipe bắt đầu trang mới sau TOC.
4. Bìa đúng composition đã duyệt, chỉ dùng monochrome, có logo chính thức duy nhất trên cover và dòng `Xuất bởi R&D Recipe Hub`.
5. Mapping/media nằm ở dịch vụ dùng chung; `IndexModel` không sao chép logic từ Preview; single export vẫn giữ hợp đồng HTTP/nội dung nhưng palette recipe được chuẩn hóa về monochrome theo quy chuẩn dự án.
6. Giới hạn concurrency/byte/dimension/frame/pixel/timeout được test bằng hành vi; ảnh lỗi biến thành fallback, request cancellation dừng công việc và temp files luôn được dọn.
7. Test PDF mở được bằng PdfPig, kiểm tra nội dung/thứ tự/page boundary/metadata; nhận diện logo được kiểm tra bằng render vùng cover hoặc fixture phân biệt được, không đếm graphics chung; HTTP integration thực sự kiểm tra antiforgery và lỗi binder, không dùng test đọc source text.
8. Smoke trên app thật với 50 recipe có hero + ảnh bước hoàn thành không OOM, không treo, không giữ file tạm và không tăng bộ nhớ kéo dài sau hai lần xuất.
9. `docs/materials/MEDIA_AND_AI_OPERATIONS.md` mô tả vận hành batch export, asset logo, giới hạn media và xử lý sự cố.
