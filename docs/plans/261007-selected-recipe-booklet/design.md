# Xuất PDF tổng hợp công thức đã chọn

## Tổng quan

Bổ sung xuất một PDF duy nhất từ nhiều công thức do người dùng chọn tại trang danh sách. Giữ nguyên luồng xuất PDF đơn lẻ hiện tại.

## Yêu cầu đã chốt

- Thêm checkbox đầu mỗi dòng công thức tại `Recipes/Index`.
- Checkbox chưa sẵn sàng bị khóa; điều kiện sẵn sàng vẫn là ít nhất một nguyên liệu và một bước.
- `Chọn tất cả` chỉ chọn công thức đủ điều kiện trong trang hiện tại.
- Danh sách hiện chưa phân trang, nên phạm vi hiện tại tương đương toàn bộ danh sách đang hiển thị, theo `Id` giảm dần.
- Nút xuất chỉ bật khi đã chọn ít nhất một công thức.
- Tối đa 50 công thức mỗi lần xuất; kiểm tra tại UI và server.
- Một file PDF duy nhất; thứ tự công thức giữ đúng thứ tự danh sách.
- Mỗi công thức bắt đầu ở trang mới.
- Luồng xuất từng công thức tại Preview không thay đổi.

## Các phương án đã đánh giá

### Tạo một QuestPDF document tổng hợp — chọn

- Dùng chung component bố cục công thức hiện tại.
- Kiểm soát được bìa, mục lục, page break và metadata.
- Không thêm thư viện ghép PDF.

### Tạo từng PDF rồi ghép — loại

- Cần thư viện ghép PDF khác.
- Tốn CPU/RAM và khó đồng nhất trang bìa, số trang.

### Ghép PDF tại trình duyệt — loại

- Tăng phụ thuộc JavaScript và RAM phía client.
- UX lỗi mạng khó kiểm soát, không phù hợp Razor Pages SSR hiện tại.

## UX danh sách

- Form `POST` có antiforgery; không truyền danh sách ID qua query string.
- Mỗi checkbox gửi ID công thức đã chọn.
- Checkbox tiêu đề chọn/bỏ chọn tất cả dòng đủ điều kiện đang hiển thị.
- Hiển thị số lượng đã chọn và giới hạn `x/50`.
- Khi vượt giới hạn, UI không cho chọn thêm và giải thích giới hạn.
- Nút `Xuất PDF đã chọn` dùng hệ thống `.btn-mobbin-*` và palette monochrome.

## Hợp đồng server

1. Nhận danh sách ID theo đúng thứ tự giao diện gửi.
2. Từ chối yêu cầu rỗng, quá 50 ID hoặc có ID trùng.
3. Truy vấn lại database; client và trạng thái checkbox không phải authority.
4. Từ chối toàn bộ nếu ID không tồn tại hoặc bất kỳ công thức nào không còn sẵn sàng.
5. Tải dữ liệu nguyên liệu, bước và media của các công thức hợp lệ.
6. Sinh PDF trong bộ nhớ và trả `application/pdf`.
7. Tên file: `recipe-collection-{yyyyMMdd-HHmm}.pdf`.

Không âm thầm bỏ công thức lỗi hoặc tự sửa input.

## Bìa PDF

Tham khảo composition trang đầu của `docs/materials/Bộ công thức Phê La Update 13_07_2026.pdf`:

- Khổ ngang.
- Mảng cọ lớn bên trái chứa tiêu đề.
- Logo Phê La lớn bên phải.
- Đơn vị và metadata nhỏ ở góc dưới trái.
- Giữ khoảng trắng rộng và phân cấp giống tài liệu tham khảo.

Nội dung:

```text
BỘ HƯỚNG DẪN
PHA CHẾ SẢN PHẨM

Phòng đào tạo - Phê La
Xuất bởi R&D Recipe Hub • {ngày xuất} • {số thực tế} công thức
```

Ràng buộc:

- Palette monochrome theo `docs/materials/DESIGN.md`.
- Logo Phê La chỉ xuất hiện trên bìa.
- Cần logo chính thức dạng SVG hoặc PNG nền trong suốt. Không crop mù từ PDF mẫu để ship nếu chưa có asset được phép dùng.
- Nếu chỉ có logo trong PDF tham khảo, việc trích xuất phải được xác nhận quyền sử dụng và kiểm tra chất lượng in.

## Mục lục và nội dung

- Trang sau bìa là danh sách công thức đã chọn, đánh số theo thứ tự xuất.
- Sau mục lục, mỗi công thức bắt đầu ở trang mới.
- Bố cục từng công thức dùng lại thiết kế PDF hiện tại; nội dung giữ nguyên, palette được chuẩn hóa về monochrome theo quy chuẩn bắt buộc của dự án.

## Hiệu năng và độ ổn định

Giới hạn 50 làm tải ảnh tăng mạnh so với xuất đơn lẻ. Cần:

- Tải ảnh với concurrency giới hạn.
- Timeout riêng cho media remote.
- Resize/compress ảnh theo kích thước hiển thị trước khi đưa vào QuestPDF.
- Không giữ ảnh gốc có độ phân giải dư thừa trong bộ nhớ.
- Giữ hành vi fallback khi media không tải được; không làm hỏng toàn bộ PDF chỉ vì một ảnh lỗi.
- Smoke test trường hợp 50 công thức có ảnh để đo thời gian, kích thước file và peak memory trước khi coi là hoàn tất.

## Rủi ro

- Chưa có logo Phê La chính thức trong repository.
- 50 công thức nhiều ảnh có thể gây timeout hoặc tăng RAM nếu không tối ưu media.
- Trang Index chưa phân trang; semantics “trang hiện tại” phải được giữ khi phân trang được bổ sung sau này.
- Tính năng là thay đổi phạm vi so với kế hoạch lõi ban đầu vốn chỉ hỗ trợ xuất từng công thức.

## Tiêu chí chấp nhận

- Chọn 1–50 công thức sẵn sàng và tải đúng một PDF.
- Bìa đúng composition đã duyệt, có logo Phê La và dòng `Xuất bởi R&D Recipe Hub`.
- Metadata hiển thị ngày xuất và số công thức thực tế.
- Mục lục và phần nội dung có cùng thứ tự với danh sách.
- Mỗi công thức bắt đầu ở trang mới.
- Công thức chưa sẵn sàng không thể chọn; server vẫn kiểm tra lại toàn bộ.
- Request rỗng, quá 50, ID trùng, ID mất hoặc công thức không còn sẵn sàng bị từ chối toàn bộ với thông báo rõ.
- Xuất PDF đơn lẻ tiếp tục hoạt động.
- Kiểm chứng được kịch bản 50 công thức có ảnh mà không lỗi hoặc dùng bộ nhớ mất kiểm soát.

## Phụ thuộc

- Logo Phê La chính thức dạng SVG hoặc PNG nền trong suốt, có quyền sử dụng.
- QuestPDF hiện có.
- Dịch vụ tải media hiện có; cần bổ sung xử lý resize/compress và concurrency giới hạn trong kế hoạch triển khai.

## Bước tiếp theo

Lập kế hoạch triển khai chi tiết bằng `ck:plan`, sau đó mới sửa code.