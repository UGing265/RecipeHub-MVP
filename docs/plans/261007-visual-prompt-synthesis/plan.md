# Plan: Visual Scene Synthesis for AI Beverage Images

## Bối cảnh & Vấn đề
1. **Lỗi Voi Hồng (Pink Elephant Effect)**: Prompt hiện tại cố cấm "NO coffee, NO dark boba pearls, NO brown sugar syrup" khiến FLUX bắt trúng các token này và vẽ đáy màu nâu, thậm chí rải hạt cà phê dưới sàn.
2. **Dịch thô nguyên văn**: LLM hiện tại dịch thô từng dòng "INGREDIENTS: ... ALL STEPS: ...", FLUX bị ngợp thông tin công thức thay vì nhận được một bản miêu tả thị giác của bức ảnh.
3. **Yêu cầu người dùng**:
   - **Ảnh bìa / Thành phẩm (Hero Shot)**: LLM tổng hợp toàn bộ công thức thành **mô tả thị giác làm đẹp ảnh bìa** (chỉ tả vẻ ngoài ly nước: màu sắc tự nhiên theo nguyên liệu, tầng lớp, đá viên, bọt kem, ly thủy tinh, phóng to cận cảnh). Không chứa bảng số đo g/ml, không chứa tên người/tên riêng, không chứa từ cấm phản tác dụng.
   - **Ảnh từng bước (Step Shot)**: LLM tổng hợp thành **mô tả hành động pha chế trực tiếp** của bước hiện tại (động tác tay chuyên gia barista đang đổ, khuấy, lắc shaker hoặc trang trí, dựa trên bối cảnh các bước trước).

## Các bước triển khai

### Bước 1: Chuẩn hóa System Instruction (AiPromptTranslationConstants)
- Cập nhật `FaithfulSystemInstruction` thành chỉ thị chuyên biệt chuyển đổi Recipe sang **Visual Scene Description**:
  - Khi `TARGET: FINAL PRODUCT HERO`: Viết một đoạn văn tiếng Anh cô đọng miêu tả trực quan diện mạo ly đồ uống hoàn chỉnh đẹp mắt (màu sắc tự nhiên của nguyên liệu, bọt sữa/trà, đá viên trong ly thủy tinh trong suốt, giọt nước đọng). Tuyệt đối không sinh bullet list, không sinh định lượng số đo, không sinh tên riêng.
  - Khi `TARGET: STEP ACTION`: Viết một đoạn văn tiếng Anh tập trung vào hành động thao tác cụ thể của chuyên gia pha chế ở bước hiện tại (đôi bàn tay cầm dụng cụ, hành động rót, khuấy, lắc, rắc bột).

### Bước 2: Dọn sạch cấm phản tác dụng trong AiImagePromptBuilder
- Xóa bỏ hoàn toàn các từ gây nhiễu ("coffee", "boba", "brown sugar", "unauthorized dark brown layer").
- Tinh chỉnh `FinalProductCanonicalStyle`: Macro tight shot, phóng to cận cảnh ly đồ uống chiếm trọn khung hình, studio trơn, đổ bóng sàn mềm mại.
- Tinh chỉnh `StepCanonicalStyle`: Tập trung góc quay thao tác barista, bàn bar sạch sẽ, ánh sáng studio chân thực.

### Bước 3: Cập nhật kiểm thử và xác minh
- Cập nhật `AiImagePromptBuilderTests` để khớp với logic miêu tả thị giác mới.
- Chạy toàn bộ test suite để đảm bảo không lỗi hồi quy.
- Tuyệt đối không commit git theo yêu cầu của người dùng.
