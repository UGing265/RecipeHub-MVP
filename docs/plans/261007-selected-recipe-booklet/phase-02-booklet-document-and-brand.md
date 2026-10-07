# Phase 2: QuestPDF booklet, bìa, mục lục và nhận diện chính thức

## Liên kết

- Kế hoạch: [plan.md](./plan.md)
- Thiết kế duyệt: [design.md](./design.md)
- Phase trước: [phase-01-shared-pdf-model-and-media.md](./phase-01-shared-pdf-model-and-media.md)
- Phase kế tiếp: [phase-03-index-selection-and-post-contract.md](./phase-03-index-selection-and-post-contract.md)
- Tài liệu tham khảo composition: `docs/materials/Bộ công thức Phê La Update 13_07_2026.pdf`, trang 1

## Mục tiêu

Tách bố cục một recipe thành component QuestPDF dùng lại được; thêm document tổng hợp gồm cover, TOC và từng recipe mở ở trang mới. Logo phải là asset Phê La chính thức, chỉ xuất hiện trên cover.

## Điều kiện đầu vào bắt buộc

File `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg` phải được nhận từ nguồn thương hiệu có quyền sử dụng. Người triển khai ghi nguồn/quyền sử dụng trong lịch sử thay đổi hoặc hồ sơ asset của dự án, mở SVG để kiểm tra viewBox, nền trong suốt và độ nét in. Không crop logo từ PDF mẫu, không trace/dựng lại, không dùng chữ `Phê La` thay logo, không ship placeholder. Thiếu asset này thì phase chưa đạt và export booklet phải báo lỗi cấu hình thay vì tạo bìa giả.

## File ảnh hưởng

Project root tuyệt đối: `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi`.

| Thao tác | Tương đối | Tuyệt đối |
|---|---|---|
| Modify | `src/RecipeCard.Web/Pdf/RecipePdfDocument.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pdf/RecipePdfDocument.cs` |
| Create | `src/RecipeCard.Web/Pdf/RecipePdfPageComposer.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pdf/RecipePdfPageComposer.cs` |
| Create | `src/RecipeCard.Web/Pdf/RecipeBookletPdfDocument.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pdf/RecipeBookletPdfDocument.cs` |
| Create/dependency | `src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/wwwroot/images/brand/phe-la-logo.svg` |
| Modify | `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` |

## Kiến trúc QuestPDF

### Component recipe dùng chung

Di chuyển `ComposeHeader`, `ComposeContent`, visual rail, ingredient table, ba layout step, image và footer từ `RecipePdfDocument` sang `RecipePdfPageComposer`. API tối thiểu:

```csharp
internal sealed class RecipePdfPageComposer(RecipePdfModel model)
{
    public void Configure(PageDescriptor page);
}
```

`Configure` đặt A4 landscape, margin, font, header/content/footer như document hiện tại. `RecipePdfDocument.Compose` chỉ đăng ký font và gọi component này cho một `container.Page`. `RecipeBookletPdfDocument.Compose` gọi cùng component trong vòng lặp; mỗi lần gọi `container.Page` tạo một section mới, nên recipe kế tiếp không tiếp tục trên trang đang dang dở.

Không copy các hàm bố cục vào booklet. `EnsureFontsRegistered` được đưa vào helper dùng chung hoặc giữ static internal để cả hai document gọi đúng một lần. Việc trích component giữ bố cục/nội dung recipe nhưng chuẩn hóa palette dùng chung về monochrome bắt buộc.

### Palette PDF dùng chung

Cover, TOC và component recipe dùng monochrome theo `docs/materials/DESIGN.md`:

- ink `#141414`
- ink-soft `#262626`
- text-muted `#707070`
- text-faint `#ADADAD`
- canvas `#FFFFFF`
- canvas-soft `#F3F3F3`
- hairline `#E0E0E0`
- hairline-soft `#F0F0F0`

Loại bỏ các màu nâu hiện tại (`#3B2A1F`, `#9A5B20`, `#E9C9A5`, ...). Không dùng Bootstrap success/warning hoặc shadow. Vì PDF đơn và booklet gọi cùng component, cả hai đồng bộ palette monochrome; route, nội dung và bố cục PDF đơn không đổi.

### Model và constructor booklet

Trong `RecipeBookletPdfDocument.cs`:

```csharp
public sealed record RecipeBookletPdfModel(
    IReadOnlyList<RecipePdfModel> Recipes,
    DateTimeOffset ExportedAt,
    string OfficialLogoSvgPath);

public sealed class RecipeBookletPdfDocument(RecipeBookletPdfModel model) : IDocument;
```

Constructor guard:

- `Recipes.Count` phải nằm trong 1..50;
- mọi model có title, ít nhất một ingredient và một step;
- `OfficialLogoSvgPath` tồn tại, extension `.svg`, nội dung đọc được;
- guard ném `InvalidOperationException` với thông điệp cấu hình logo rõ ràng; không fallback.

### Bìa

- A4 landscape, nền trắng, margin rộng.
- Cột trái khoảng 58%: mảng cọ đen dạng vector bất đối xứng, không dùng ảnh màu; text trắng `BỘ HƯỚNG DẪN` và `PHA CHẾ SẢN PHẨM` theo hai cấp cỡ chữ.
- Cột phải khoảng 42%: render nguyên SVG chính thức bằng QuestPDF `Svg`, `FitArea`, giữ aspect ratio; không crop.
- Góc dưới trái:
  - `Phòng đào tạo - Phê La`
  - `Xuất bởi R&D Recipe Hub • {ExportedAt:dd/MM/yyyy} • {Recipes.Count} công thức`
- Không có logo ở header/footer/recipe page/TOC.

### Mục lục

- Bắt đầu ngay sau cover, A4 landscape.
- Heading `MỤC LỤC`, danh sách `01`, `02`, ... và title theo đúng index `Recipes`.
- Dùng row có thể wrap; QuestPDF tự phân trang khi 50 tên dài không nằm gọn một trang. Không cắt tên, không ép TOC thành đúng một trang.
- Recipe đầu bắt đầu ở page section mới sau trang TOC cuối. Không thêm số trang giả vì recipe có thể tràn nhiều trang.
- Footer mục lục chỉ có `R&D Recipe Hub` và page number; không có logo.

### Nội dung và metadata

- Sau TOC, foreach `Recipes` gọi `RecipePdfPageComposer.Configure` theo thứ tự model.
- `GetMetadata()`:
  - `Title = "Bộ hướng dẫn pha chế sản phẩm"`
  - `Author = "R&D Recipe Hub"`
  - `Creator = "R&D Recipe Hub"`
  - `Subject = $"{Recipes.Count} công thức"`
  - `CreationDate = ExportedAt` để giữ nguyên offset.
- Không đổi text ingredient, step, note, nhãn ảnh AI hoặc bố cục trong component recipe; palette chuyển về monochrome theo bảng trên.

## Các bước triển khai

- [ ] **1. Đặt asset logo chính thức.** Tạo đúng path, xác nhận SVG nền trong suốt/viewBox/quyền dùng và đảm bảo publish output chứa asset static.
- [ ] **2. Viết test booklet thất bại trước.** Dùng logo fixture phân biệt được, hai model đơn giản, PdfPig đọc cover/TOC/page recipe/metadata.
- [ ] **3. Trích `RecipePdfPageComposer`.** Di chuyển nguyên logic recipe, không để hai bản compose song song; sửa `RecipePdfDocument` thành wrapper mỏng.
- [ ] **4. Chuẩn hóa palette PDF dùng chung.** Tập trung constants monochrome; loại bỏ toàn bộ literal màu nâu khỏi các lớp PDF.
- [ ] **5. Cài `RecipeBookletPdfDocument`.** Guard input, cover, TOC có thể phân trang, mỗi recipe là page section riêng, metadata đúng contract.
- [ ] **6. Giữ regression PDF đơn.** Test title, ingredient/step cuối, landscape, palette monochrome và hero/step fallback tiếp tục qua PdfPig/GeneratePdf.

## Test hành vi bắt buộc

Trong `SelectedRecipeBookletExportTests.cs`:

1. `Booklet_has_cover_then_toc_then_recipe_sections` — PDF hai recipe có cover page 1, TOC bắt đầu page 2 và recipe đầu chỉ xuất hiện sau TOC.
2. `Booklet_toc_and_recipe_sections_preserve_model_order` — PdfPig đọc text và so vị trí `Id DESC` đã đưa vào model.
3. `Booklet_cover_contains_required_copy_and_logo_region` — render page cover rồi đối chiếu vùng logo với fixture/golden có tolerance; không dùng số lượng graphics chung làm bằng chứng.
4. `Official_logo_is_visually_absent_after_cover` — render vùng logo tương ứng trên page TOC/recipe hoặc kiểm tra bằng fixture nhận diện được; smoke với asset chính thức là bằng chứng cuối.
5. `Booklet_metadata_contains_hub_author_count_and_injected_date_offset`.
6. `Booklet_with_fifty_max_length_titles_paginates_toc_without_losing_titles` — đủ 50 title; recipe đầu bắt đầu ở page sau TOC cuối, không giả định tổng page cố định.
7. `Missing_or_unreadable_logo_fails_with_configuration_error` — chứng minh không có fake fallback.
8. `Recipe_section_with_missing_images_still_generates`.

Trong `RecipePdfExportTests.cs`, giữ test PDF đơn cho 2/4/7/20 bước, palette monochrome và test ảnh thiếu. Test PDF parser phải kiểm tra file mở được và chứa dữ liệu nghiệp vụ, không chỉ `bytes.Length > 0`.

Không chạy test/build riêng ở phase này; Phase 4 chạy một lần sau khi tích hợp.

## Tiêu chí hoàn thành Phase 2

- Chỉ có một implementation cho bố cục recipe và được cả hai document gọi.
- Cover/TOC/content đúng thứ tự và mọi trang landscape.
- Palette PDF không còn màu ngoài dải monochrome đã chốt.
- Logo chính thức render nguyên tỷ lệ, chỉ trên cover; thiếu logo không sinh booklet.
- PDF đơn vẫn sinh từ component dùng chung và giữ nguyên dữ liệu/response contract.
