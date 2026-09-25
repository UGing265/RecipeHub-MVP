# AGENTS.md — R&D Recipe Hub Guidelines

Hướng dẫn quy chuẩn thiết kế, kiến trúc kỹ thuật và quy tắc code dành cho AI Agents khi làm việc trong repository này (đồng thời áp dụng cho file `AGENT.md`).

---

## 1. Hệ thống thiết kế (Visual Design System — `doc/DESIGN.md`)

Mọi giao diện web (Razor Pages) và tài liệu xuất bản (QuestPDF) **BẮT BUỘC** tuân thủ triệt để ngôn ngữ thiết kế từ `doc/DESIGN.md` (phong cách Mobbin / Gallery-White Monochrome).

### Bố cục khung ứng dụng (App Shell & Sidebar Navigation)
- Thanh điều hướng chính của hệ thống được tổ chức dạng **Sidebar bên trái** (`.app-sidebar`, chiều rộng 260px) theo quy chuẩn `ex-app-shell-row` trong `doc/DESIGN.md`:
  - Mục điều hướng "Nguyên liệu" (`/Ingredients/Index`) và "Công thức" (`/Recipes/Index`) nằm cố định trên sidebar.
  - Mỗi hàng điều hướng (`.app-nav-item`) có bo góc 16px (`--radius-sm`), padding `8px 16px`, vạch chỉ báo trạng thái kích hoạt (`activeIndicator: #141414`) ở cạnh trái.
  - Hỗ trợ responsive trên thiết bị di động thông qua nút mở menu `.app-topbar` và backdrop mờ.

### Bảng màu (Monochrome Palette)
- **Primary / Ink**: `#141414` (nền tối, chữ tiêu đề, CTA chính, footer, active indicator)
- **Ink Soft**: `#262626` (nội dung chính)
- **Text Muted**: `#707070` (nhãn phụ, ghi chú)
- **Text Faint**: `#adadad` (divider, text trên nền tối)
- **Canvas**: `#ffffff` (nền trang chính, thẻ trắng)
- **Canvas Soft**: `#f3f3f3` (nền khối phụ, thanh navigation)
- **Field**: `#f0f0f0` (nền ô nhập liệu input / select)
- **Hairline**: `#e0e0e0` (đường kẻ phân cách 1px)
- **Hairline Soft**: `#f0f0f0` (đường kẻ nhẹ bên trong khối)
- **Accent**: `#0066ff` (**CHỈ DÙNG DUY NHẤT** cho tín hiệu thương mại đặc biệt như gói mua/quyết định mua; không dùng cho badge thông thường hay nút thao tác).

### Hình học & Bo góc (Geometry & Radii)
- **Stadium Pill (`9999px`)**: Áp dụng cho **tất cả** thành phần tương tác:
  - Tất cả các nút bấm (`.btn-mobbin-primary`, `.btn-mobbin-outline`, `.btn-mobbin-soft`, `.btn-mobbin-danger`)
  - Badges và nhãn trạng thái (`.badge-pill-soft`, `.badge-pill-dark`)
- **Containers / Thẻ (`24px` - `--radius-md`)**: Áp dụng cho các card nội dung (`.mobbin-card`, `.mobbin-card-soft`, `.mobbin-table-container`, `.recipe-sheet-card`).
- **Media & Inputs (`16px` - `--radius-sm`)**: Áp dụng cho ô nhập liệu (`.form-control`, `.form-select`) và ảnh minh họa bước thực hiện (`.step-media-tile`).
- **Icon Squircle (`30%`)**: Biểu tượng thương hiệu / icon app tile (`.mobbin-squircle`).

### Nguyên tắc Shadow-Free (Không đổ bóng)
- **TUYỆT ĐỐI KHÔNG** dùng `box-shadow` trên card, sheet preview, nút hay thanh điều hướng.
- Phân tầng thị giác bằng các sắc độ trung tính (ladder of neutral tints: Canvas → Canvas Soft → Field) và đường kẻ mảnh 1px (`Hairline`).

### Typography & Quyết định Font chữ (Saans & Fallback)
- `doc/DESIGN.md` chỉ định phông chữ **Saans** (Pangram Pangram). Do đây là font thương mại và repository hiện chưa nhúng file bản quyền `.woff2`, hệ thống sử dụng **System Sans Fallback Stack**:
  ```css
  --font-family-base: "Saans", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
  ```
- Duy trì nghiêm ngặt các thông số:
  - Headings: Trọng số **652**, line-height chặt 1.1–1.25, letter-spacing `-0.01em`.
  - Body: Trọng số **456**, line-height 1.38–1.45.
  - Buttons / Labels: Trọng số **600**.

### Xử lý trạng thái Hủy/Xóa & Báo lỗi (Monochrome Feedback)
- Không dùng màu đỏ hay vàng chói cho nút hủy hoặc cảnh báo.
- Nút xóa (`.btn-mobbin-danger`): Dùng viền hairline, nền canvas, chữ ink khi hover.
- Thông báo lỗi xác thực form (`.text-danger`): Được ghi đè thành chữ ink đậm kèm ký hiệu `—` phía trước, input lỗi có nền `var(--color-canvas-soft)` và viền ink 1px.

---

## 2. Kiến trúc & Công nghệ (Technical Stack)

- **Framework**: .NET 10, ASP.NET Core Razor Pages (SSR), C# 13.
- **Database**: SQLite cục bộ (`src/RecipeCard.Web/recipe-card.db`), kết nối qua Entity Framework Core.
- **Export PDF**: QuestPDF (tạo file PDF trực tiếp từ bộ nhớ, không qua headless browser).
- **Upload File**: `ImageStorageService`, lưu ảnh tại `wwwroot/uploads/steps/`, kiểm tra magic bytes `.jpg`, `.png`, `.webp`, tối đa 5MB.

### Ràng buộc dữ liệu & Toàn vẹn (Data Integrity)
1. **Tiếng Việt không phân biệt hoa thường**:
   - SQLite `NOCASE` chỉ hỗ trợ ASCII. Bắt buộc duy trì cột `NormalizedName` (`ToUpperInvariant().Trim()`) và index Unique trên `NormalizedName`.
2. **Kiểm tra định lượng số dương**:
   - EF Core lưu `decimal` trong SQLite dưới dạng TEXT. Bắt buộc dùng check constraint `CAST(Quantity AS REAL) > 0` trong `RecipeDbContext`.
3. **Bảo vệ toàn vẹn tham chiếu nguyên liệu**:
   - Khóa ngoại `RecipeIngredients.IngredientId` cấu hình `DeleteBehavior.Restrict` hoặc `NoAction`. Khi xóa nguyên liệu, phải kiểm tra xem có công thức nào đang sử dụng hay không trước khi xóa.
4. **Đổi thứ tự bước (Step Reordering)**:
   - Unique index trên `(RecipeId, SortOrder)` yêu cầu đổi thứ tự qua 2 bước (gán giá trị tạm âm) bên trong Transaction để tránh vi phạm Unique constraint.
5. **Điều kiện xuất PDF**:
   - Thẻ công thức chỉ được xuất PDF khi đạt trạng thái sẵn sàng: $\ge 1$ nguyên liệu và $\ge 1$ quy trình bước.

---

## 3. Điều cấm kỵ đối với Agents (Prohibitions)

- ❌ **Không** sử dụng các màu mặc định của Bootstrap (`btn-primary` xanh dương, `btn-success` xanh lá, `btn-danger` đỏ chói, `btn-warning` vàng cam). Bắt buộc dùng hệ thống `.btn-mobbin-*`.
- ❌ **Không** dùng emoji trang trí (📦, 📝, 🚀, v.v.) trong tiêu đề, thẻ card hoặc nút bấm.
- ❌ **Không** dùng `box-shadow` để tạo độ nổi; chỉ dùng tint background và hairline 1px.
- ❌ **Không** lạm dụng màu xanh `#0066ff` cho các badge hay nút thông thường.
- ❌ **Không** tự ý chuyển sang Single Page Application (SPA), React/Vue hoặc thêm Web API riêng trừ khi có yêu cầu rõ ràng.
- ❌ **Không** đổi layout PDF QuestPDF sang màu sắc khác ngoài palette Monochrome `#141414` đã thiết lập.
- ❌ **Không** xóa code kiểm tra magic bytes của ảnh hoặc bỏ qua việc xử lý transaction khi đổi thứ tự bước.
