# Phase 4: Regression, HTTP integration, smoke 50 recipe và tài liệu vận hành

## Liên kết

- Kế hoạch: [plan.md](./plan.md)
- Thiết kế duyệt: [design.md](./design.md)
- Phase trước: [phase-03-index-selection-and-post-contract.md](./phase-03-index-selection-and-post-contract.md)

## Mục tiêu

Khóa hợp đồng bằng test hành vi và kiểm chứng luồng thật trên app: antiforgery, batch 1/50, reject toàn-or-nothing, PDF mở được, media bị giới hạn, xuất đơn không regression, memory/temp cleanup. Sau đó cập nhật tài liệu vận hành.

## File ảnh hưởng

Project root tuyệt đối: `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi`.

| Thao tác | Tương đối | Tuyệt đối |
|---|---|---|
| Modify | `src/RecipeCard.Web/Program.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/src/RecipeCard.Web/Program.cs` |
| Modify | `tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj` |
| Modify | `tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfExportTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipePdfModelFactoryTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/SelectedRecipeBookletExportTests.cs` |
| Create | `tests/RecipeCard.Web.Tests/RecipeIndexAntiforgeryTests.cs` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/tests/RecipeCard.Web.Tests/RecipeIndexAntiforgeryTests.cs` |
| Modify | `docs/materials/MEDIA_AND_AI_OPERATIONS.md` | `D:/AShiroru/LessonFPT/semester 8/project-prn-prm/mvp-mini-cot-loi/docs/materials/MEDIA_AND_AI_OPERATIONS.md` |

## Chiến lược test

### 1. Unit/integration với SQLite + PDF parser

Dùng SQLite thật in-memory như test hiện có, ImageSharp tạo ảnh JPEG/PNG thật và PdfPig mở PDF thật. Không dùng test tìm chuỗi trong source, chỉ assert `bytes.Length`, hoặc mock QuestPDF.

Phân bổ:

- `RecipePdfModelFactoryTests.cs`: limit concurrency, timeout, byte/dimension, resize, deterministic budget, cancellation, temp cleanup.
- `SelectedRecipeBookletExportTests.cs`: validation handler, `Id DESC`, cover/TOC/page boundary, metadata, 1/50 recipe, missing image, missing logo.
- `RecipePdfExportTests.cs`: regression endpoint đơn, adaptive layouts, hero/step image và filename cũ.

Các assertion PDF tối thiểu:

- PdfPig mở file không lỗi;
- mọi page landscape;
- cover text và metadata đúng;
- TOC chứa đúng N title theo thứ tự và được phép phân trang;
- recipe title/ingredient/step nằm ở section sau trang TOC cuối;
- first page của mỗi recipe tăng nghiêm ngặt, không cùng page với recipe trước;
- ảnh lỗi không làm mất text hoặc recipe;
- 50 recipe tên dài không làm lỗi layout hoặc mất mục lục; không giả định tổng page cố định.

### 2. HTTP integration antiforgery

Thêm package test `Microsoft.AspNetCore.Mvc.Testing` version `10.0.12`. Cuối `Program.cs` thêm `public partial class Program;` để `WebApplicationFactory<Program>` host app thật. Test factory dùng file SQLite tạm riêng, override connection string trước startup và xóa file khi dispose; không chạm `recipe-card.db` của người dùng.
`RecipeIndexAntiforgeryTests.cs`:

1. GET `/Recipes` qua `WebApplicationFactory` và đọc HTML response thực.
2. Xác nhận batch form action `/Recipes?handler=ExportSelected`, method POST và có hidden `__RequestVerificationToken` không rỗng.
3. POST form-urlencoded có ID hợp lệ nhưng không token/cookie: HTTP 400, không phải PDF.
4. GET lại để lấy token + cookie, POST ID hợp lệ: HTTP 200, `Content-Type: application/pdf`, `Content-Disposition` đúng filename và PdfPig mở body.
5. POST có token hợp lệ với `SelectedRecipeIds=abc`, rồi `SelectedRecipeIds=1&SelectedRecipeIds=abc`: HTTP 400, đúng message binder và không có PDF.
6. GET markup với một ready và một not-ready recipe: checkbox ready enabled, not-ready disabled; delete action vẫn có form riêng và token.
7. POST rỗng với token: HTTP 400 và HTML response chứa message contract; đây là response app thực, không phải source-text assertion.

Nếu startup seed làm nhiễu data, factory tạo database tạm rồi seed ID test có title duy nhất; query/assert bằng ID cụ thể, không phụ thuộc tổng row.

### 3. Client interaction

Không dùng unit test JavaScript giả DOM. Smoke browser ở phần dưới kiểm tra select-all, giới hạn thứ 51, counter, submit disabled và state sau HTTP 400 trên markup thật.

## Ma trận regression bắt buộc

| Luồng | Bằng chứng |
|---|---|
| Export đơn ready | `%PDF-`, PdfPig mở được, đúng title/content type/slug filename |
| Export đơn not-ready | redirect Preview + message cũ |
| Export đơn media lỗi | vẫn có PDF và đầy đủ text |
| Batch rỗng/>50/trùng/missing/not-ready | HTTP 400, message đúng, factory không chạy |
| Batch 1 | cover + TOC + recipe, filename timestamp |
| Batch 50 | đủ 50 TOC/content; TOC dài tự phân trang, không mất recipe cuối |
| Input đảo thứ tự | output `Id DESC` |
| Antiforgery thiếu | HTTP 400 từ app thật |
| Binder nhận ID không phải số | HTTP 400 đúng message, không sinh PDF |
| Media timeout/quá byte/quá pixel/nhiều frame | ảnh fallback; booklet còn hợp lệ |
| Request cancellation | `OperationCanceledException`, temp sạch |
| Logo thiếu | lỗi cấu hình, không PDF giả |

## Các bước thực hiện và lệnh kiểm tra

- [ ] **1. Hoàn tất test của ba phase trước.** Không thay assertion nghiệp vụ thành snapshot/source-string.
- [ ] **2. Thêm WebApplicationFactory test.** Cô lập DB/file temp và giữ cookie/token thật qua GET/POST.
- [ ] **3. Chạy toàn bộ suite một lần:**

```powershell
dotnet test tests/RecipeCard.Web.Tests/RecipeCard.Web.Tests.csproj
```

Kỳ vọng: exit code 0, không test fail/skip mới.

- [ ] **4. Build publish-path một lần:**

```powershell
dotnet build src/RecipeCard.Web/RecipeCard.Web.csproj --configuration Release
```

Kỳ vọng: exit code 0, không warning mới; kiểm tra output có `wwwroot/images/brand/phe-la-logo.svg`.

- [ ] **5. Chạy app và smoke browser theo checklist dưới.** Ghi elapsed time, output size, peak working set/GC heap và trạng thái temp vào ghi chú PR/phiếu nghiệm thu, không commit file PDF test.
- [ ] **6. Cập nhật tài liệu vận hành.** Sau smoke đạt mới đánh dấu feature sẵn sàng phát hành.

## Smoke trên app thật

### Chuẩn bị dữ liệu

Dùng một bản sao database phát triển, không dùng production. Qua các màn hình Create/Edit/Media hiện có, chuẩn bị **51 recipe ready** có ID liên tiếp để kiểm tra cap; mỗi recipe có:

- ít nhất 2 ingredients;
- 2 steps ngắn;
- một hero image 3.000 × 2.000;
- ít nhất một step image 3.000 × 2.000;
- title duy nhất `SMOKE BOOKLET 01` … `SMOKE BOOKLET 51`.

Asset ảnh phải là file test không nhạy cảm và dưới 8 MiB. Không commit database/ảnh/output smoke. Đảm bảo logo chính thức đã có ở path publish.

### Khởi chạy và đo

```powershell
dotnet run --project src/RecipeCard.Web/RecipeCard.Web.csproj
```

Từ terminal khác, lấy PID tiến trình và chạy:

```powershell
dotnet-counters monitor --process-id <PID> --counters System.Runtime
```

Ghi baseline working set và GC heap sau khi app idle 30 giây. Kiểm tra `%TEMP%/recipe-card-pdf` trước run; không xóa thư mục của tiến trình đang chạy.

### Flow browser

1. Mở `/Recipes`; xác nhận row `Id` cao hơn đứng trước, recipe not-ready (nếu có) disabled.
2. Ban đầu counter `0/50`, nút export disabled.
3. Chọn một recipe: counter `1/50`, nút bật. Bỏ chọn: trở lại `0/50`.
4. Bấm header select-all khi có 51 ready: chỉ 50 row đầu theo `Id DESC` checked, counter `50/50`, row eligible thứ 51 không chọn được và có thông báo giới hạn.
5. Bỏ một row: row thứ 51 chọn lại được; chọn nó và xác nhận vẫn đúng 50.
6. Submit bình thường; trình duyệt tải đúng một file `recipe-collection-yyyyMMdd-HHmm.pdf`.
7. Mở PDF: cover có logo chính thức và copy bắt buộc; TOC đủ 50 title theo `Id DESC`; mỗi title recipe bắt đầu trang mới; không có logo sau cover; ảnh hiển thị không méo/crop.
8. Mở tab thứ hai, làm một recipe đang chọn thành not-ready rồi submit lại tab đầu: response HTTP 400 hiển thị đúng recipe, không có file partial.
9. Dùng DevTools sửa một checkbox value thành ID không tồn tại rồi submit: HTTP 400 với message missing ID, không file.
10. Kiểm tra export đơn từ Preview cho một recipe: vẫn tải slug filename, content type PDF và nội dung/ảnh đúng.
11. Lặp lại export batch 50 lần thứ hai ngay sau lần đầu để phát hiện retention/temp leak.

### Flow antiforgery ngoài browser

Gửi POST trực tiếp không cookie/token:

```powershell
curl.exe -i -X POST "https://localhost:<port>/Recipes?handler=ExportSelected" -d "SelectedRecipeIds=1"
```

Kỳ vọng HTTP 400 và không có `application/pdf`. Flow browser bình thường ở trên phải thành công nhờ token thật.

### Ngưỡng nghiệm thu smoke 50 recipe

- Cả hai lần xuất hoàn tất trong 120 giây/lần, không OOM, không HTTP 5xx, không request treo.
- Peak working set dưới 768 MiB trên máy smoke; peak lần hai không vượt peak lần một quá 10%.
- Trong 30 giây sau mỗi download, GC heap không giữ tăng thêm quá 100 MiB so với baseline.
- Output mở được; TOC/content đủ 50 recipe; recipe đầu bắt đầu sau trang TOC cuối, không phụ thuộc tổng số trang.
- File output dưới 80 MiB với fixture trên.
- Sau response và dispose, thư mục `%TEMP%/recipe-card-pdf/{guid}` của request không còn; không còn file temp sau lần hai.
- Log có thể có warning media đơn lẻ nhưng không có URL query/token, unhandled exception hoặc lỗi cleanup.

Nếu vượt bất kỳ ngưỡng nào, không nới limit để làm test xanh. Dùng counter/log xác định giai đoạn tải, resize hay QuestPDF; sửa pipeline rồi chạy lại cùng fixture.

## Cập nhật tài liệu vận hành

Thêm section “Xuất PDF tổng hợp công thức” vào `docs/materials/MEDIA_AND_AI_OPERATIONS.md`:

- thao tác chọn 1–50 và điều kiện ready;
- POST antiforgery và toàn-or-nothing validation;
- thứ tự server `Id DESC`, filename/content type;
- cấu trúc cover/TOC/content và metadata `Xuất bởi R&D Recipe Hub`;
- path logo chính thức, yêu cầu quyền sử dụng, nền trong suốt, cách kiểm tra khi publish; cấm crop/fake fallback;
- bảng concurrency/timeout/byte/dimension/resize/global budget;
- fallback ảnh và cancellation/temp cleanup;
- runbook xử lý lỗi logo, media timeout, HTTP 400, memory vượt ngưỡng;
- checklist smoke 50 recipe và chỉ số phải ghi khi phát hành.

Không sao chép secrets hoặc URL media có query token vào docs/log.

## Tiêu chí hoàn thành Phase 4

- Toàn suite test và Release build đạt một lần sau tích hợp.
- Test antiforgery chạy qua app host thật; PDF tests đọc bằng PdfPig; không có test tautological/source-text.
- Smoke browser thực tế hoàn tất cả valid, stale not-ready, missing ID, cap 50 và single export.
- Smoke 50 recipe đạt giới hạn time/memory/output/temp đã chốt hai lần liên tiếp.
- Tài liệu vận hành phản ánh chính xác implementation cuối cùng và asset logo đã publish.
