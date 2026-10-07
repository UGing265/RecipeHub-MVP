# Phase 3: Chọn recipe trên Index và POST export toàn-or-nothing

## Liên kết

- Kế hoạch: [plan.md](./plan.md)
- Thiết kế duyệt: [design.md](./design.md)
- Phase trước: [phase-02-booklet-document-and-brand.md](./phase-02-booklet-document-and-brand.md)
- Phase kế tiếp: [phase-04-regression-smoke-and-docs.md](./phase-04-regression-smoke-and-docs.md)

## Mục tiêu

Thêm UX chọn 1–50 recipe đủ điều kiện trên `Recipes/Index`, gửi POST có antiforgery và thực thi validation server toàn-or-nothing. Server luôn truy vấn lại database và xuất theo `Id DESC`, bất kể thứ tự input.

## File ảnh hưởng

Project root tuyệt đối: `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi`.

| Thao tác | Tương đối | Tuyệt đối |
|---|---|---|
| Modify | `src/RecipeCard.Web/Program.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Program.cs` |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Index.cshtml.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pages/Recipes/Index.cshtml.cs` |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Index.cshtml` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Pages/Recipes/Index.cshtml` |
| Modify | `src/RecipeCard.Web/wwwroot/css/site.css` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/wwwroot/css/site.css` |
| Modify | `tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` |

## Page model và query

Đổi constructor `IndexModel` để nhận thêm `IRecipePdfModelFactory`, `IWebHostEnvironment`, `TimeProvider`; đăng ký `TimeProvider.System` trong DI. Giữ `IImageStorageService` cho delete.

Thêm:

```csharp
private const int MaxSelectedRecipes = 50;

[BindProperty]
public List<int> SelectedRecipeIds { get; set; } = [];
```

Tách projection GET thành `LoadRecipesAsync(CancellationToken)` và dùng cho cả `OnGetAsync` và mọi nhánh POST trả `Page()`. Query danh sách vẫn `OrderByDescending(r => r.Id)`.

Handler export:

```csharp
public async Task<IActionResult> OnPostExportSelectedAsync(CancellationToken cancellationToken)
```

Trình tự bắt buộc:

1. Nếu model binding lỗi hoặc có ID `<= 0`: reject.
2. Nếu count `0`: reject.
3. Nếu count `> 50`: reject trước khi query.
4. Nếu `Distinct().Count != Count`: reject; không tự deduplicate.
5. Query bằng `AsNoTracking().AsSplitQuery()`, filter ID và include:
   - `FinalMediaAsset`
   - `Ingredients.ThenInclude(Ingredient)`
   - `Steps.ThenInclude(MediaAsset)`
   - `OrderByDescending(Id)`
   `AsSplitQuery()` là bắt buộc để tránh cartesian explosion khi mỗi recipe có nhiều ingredient và step.
6. So input ID với kết quả; có ID thiếu thì reject toàn bộ.
7. Kiểm tra từng recipe có ít nhất một ingredient và một step; có recipe không sẵn sàng thì reject toàn bộ.
8. Gọi factory với list đã `Id DESC`; không reorder theo form.
9. Resolve logo từ `Path.Combine(webRoot, "images", "brand", "phe-la-logo.svg")`.
10. Dựng `RecipeBookletPdfModel` bằng `TimeProvider.GetLocalNow()`, sinh PDF trong memory khi batch temp còn sống.
11. Kiểm tra `RequestAborted` trước `GeneratePdf`; propagate cancellation của query/media. QuestPDF sync không hỗ trợ dừng giữa `GeneratePdf`, vì vậy không bọc exception cancellation giả.
12. Trả:

```csharp
File(
    pdfBytes,
    "application/pdf",
    $"recipe-collection-{exportedAt:yyyyMMdd-HHmm}.pdf");
```

Mọi lỗi media riêng lẻ đã fallback ở Phase 1. Lỗi logo/document là lỗi cấu hình/server và không được đổi thành PDF rỗng hoặc response 200 giả.

## Hợp đồng reject chính xác

Helper duy nhất `RejectExportAsync(string message, CancellationToken)`:

- `ModelState.Clear()` để loại message binder không ổn định;
- `ModelState.AddModelError(string.Empty, message)` đúng một lần để `asp-validation-summary="ModelOnly"` hiển thị lỗi;
- `Response.StatusCode = StatusCodes.Status400BadRequest`;
- load lại `Recipes` theo `Id DESC`;
- chỉ giữ checked cho ID tồn tại **và còn sẵn sàng**; ID thiếu/not-ready không được render checked;
- `return Page()`;
- không gọi factory, không tạo temp directory, không sinh PDF.

| Ưu tiên | Trường hợp | Message |
|---:|---|---|
| 1 | binder lỗi hoặc ID `<= 0` | `Dữ liệu lựa chọn công thức không hợp lệ.` |
| 2 | rỗng | `Hãy chọn ít nhất 1 công thức để xuất PDF.` |
| 3 | >50 phần tử | `Chỉ có thể xuất tối đa 50 công thức mỗi lần.` |
| 4 | trùng ID | `Danh sách công thức có ID trùng lặp. Vui lòng chọn lại.` |
| 5 | ID thiếu | `Không tìm thấy công thức đã chọn: {ID giảm dần, ngăn cách ", "}.` |
| 6 | not-ready | `Công thức chưa đủ điều kiện xuất PDF: {Tên} (#{Id}), ...` theo `Id DESC` |

Nếu cùng request vi phạm nhiều điều kiện, trả lỗi đầu tiên theo bảng; không gộp và không âm thầm sửa input.

## Razor markup không lồng form

`Index.cshtml` hiện có form delete trong từng row. Không bọc toàn table bằng một form vì HTML form lồng nhau không hợp lệ. Tạo một form rỗng/toolbar độc lập:

```html
<form id="recipe-booklet-form" method="post" asp-page-handler="ExportSelected">
    <div asp-validation-summary="ModelOnly" role="alert"></div>
    <!-- counter + submit -->
</form>
```

Mỗi checkbox row dùng thuộc tính `form="recipe-booklet-form"`, `name="SelectedRecipeIds"`, `value="@item.Id"`. Checkbox recipe chưa sẵn sàng có `disabled`, `aria-disabled="true"` và title giải thích; disabled field không gửi lên server. Checkbox header chỉ điều khiển UI, không có `name` và không gửi giá trị.

Thêm cột chọn ở đầu table, trước STT. Toolbar nằm trên table:

- nhãn `Đã chọn 0/50` với `aria-live="polite"`;
- mô tả `Tối đa 50 công thức sẵn sàng mỗi lần xuất`;
- nút `.btn-mobbin-primary` `Xuất PDF đã chọn`, disabled khi count = 0;
- validation summary monochrome, không dùng `.text-danger`, màu đỏ/cam hoặc shadow.

Delete form và các link Soạn/Xem trước giữ nguyên.

## JavaScript page-scoped

Đặt script trong `@section Scripts` của `Index.cshtml`, không đưa logic riêng trang vào global `site.js`.

Hành vi:

1. Lấy danh sách eligible gốc bằng class/data attribute riêng; không suy ra eligibility từ `:not(:disabled)` vì trạng thái cap 50 cũng tạm disable checkbox.
2. Mỗi thay đổi cập nhật count, button disabled, `aria-live`, trạng thái header checked/indeterminate.
3. Khi count đạt 50, disable tạm mọi checkbox eligible chưa checked bằng state/class riêng; checkbox checked vẫn cho bỏ chọn. Khi count giảm, bật lại các checkbox đó nhưng không bật checkbox vốn not-ready.
4. Nếu người dùng kích checkbox thứ 51 bằng keyboard/script event, hoàn tác ngay và hiển thị `Chỉ có thể chọn tối đa 50 công thức.`; server vẫn là authority.
5. Header “Chọn tất cả” luôn tính từ danh sách eligible gốc:
   - nếu số eligible `<= 50`, chọn tất cả;
   - nếu `> 50`, chọn 50 item đầu theo DOM (`Id DESC`) rồi mới áp trạng thái cap cho phần còn lại;
   - khi bỏ chọn, bỏ toàn bộ item eligible.
6. Khởi tạo từ state render sau POST 400; không reset checkbox đã giữ.
7. Không gọi fetch/AJAX; submit chuẩn để trình duyệt tải file.

## CSS

Thêm class semantic cho selection column, toolbar, counter, disabled row checkbox và error summary vào `site.css`. Chỉ dùng biến `--color-ink`, `--color-text-muted`, `--color-canvas`, `--color-canvas-soft`, `--color-hairline`; không thêm shadow hoặc color literal ngoài monochrome.

## Các bước triển khai

- [ ] **1. Viết test handler validation trước.** Seed SQLite cho ready/not-ready/missing; spy factory chứng minh nhánh reject không gọi export.
- [ ] **2. Tách loader danh sách và thêm bind property.** `OnGetAsync` nhận cancellation; delete handler tiếp tục hoạt động.
- [ ] **3. Cài validation theo đúng precedence/message/status.** Một DB query sau các check cú pháp; không có partial success.
- [ ] **4. Nối factory và booklet.** Dùng `await using`, path logo cố định, injected time, filename chính xác.
- [ ] **5. Sửa markup bằng external form association.** Giữ delete form hợp lệ; thêm antiforgery tự động qua form tag helper.
- [ ] **6. Thêm script selection.** Đồng bộ count/header/limit/submit và state sau 400.
- [ ] **7. Thêm CSS monochrome.** Không dùng Bootstrap contextual colors.
- [ ] **8. Bổ sung test success/order.** Input cố tình gửi thứ tự tăng dần, rồi dùng PdfPig chứng minh TOC/content là `Id DESC`.

## Test hành vi bắt buộc

Trong `SelectedRecipeBookletExportTests.cs`:

1. Empty, 51 IDs, duplicates, ID `0`, missing ID và not-ready recipe: đúng HTTP 400/message; factory spy có call count 0.
2. Request có cả duplicate và missing: trả duplicate theo precedence, không query/export phần còn lại.
3. Batch gồm ready + not-ready: reject toàn bộ, không có `FileContentResult`.
4. Input ID tăng dần/đảo ngẫu nhiên: PDF/TOC theo `Id DESC`.
4a. Batch nhiều ingredient × nhiều step dùng split query, trả đủ graph không nhân bản collection hoặc tăng result-set theo tích Descartes.
5. Batch 1 và batch 50 recipe trả `FileContentResult`, `application/pdf`, filename từ fake `TimeProvider` đúng `recipe-collection-yyyyMMdd-HHmm.pdf`.
6. Batch 50 recipe có đủ title trong TOC/content; không mất recipe cuối.
7. Logo thiếu: không trả PDF thành công và có lỗi cấu hình rõ ràng ở tầng document.
8. Existing delete handler và GET list vẫn hoạt động sau khi constructor/page model thay đổi.

Không chạy test/build riêng ở phase này; Phase 4 chạy toàn bộ sau tích hợp.

## Tiêu chí hoàn thành Phase 3

- UI chọn đúng eligible recipe, counter/limit/header/button truy cập được bằng keyboard và screen reader.
- Form batch có antiforgery và không lồng với delete form.
- Tất cả invalid cases trả HTTP 400 đúng message/precedence, không partial export.
- Server query lại đầy đủ graph và quyết định thứ tự `Id DESC`.
- Handler chỉ orchestration; mapping/media không bị sao chép từ Preview.
- Batch hợp lệ trả đúng content type, filename và dispose tài nguyên temp.
