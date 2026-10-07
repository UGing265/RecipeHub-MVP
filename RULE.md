# Quy tắc dự án & Prompt Engineering (RULE.md)

Tài liệu ghi nhớ các quy tắc kỹ thuật và bài học xương máu khi thiết kế prompt cho AI sinh ảnh (FLUX, Stable Diffusion, DALL-E) và LLM dịch/tổng hợp (Gemini, Llama) trong dự án.

---

## 1. Tránh "Hiệu ứng Voi Hồng" (The Pink Elephant Problem) & Ảo giác (Hallucination)

> **Hiện tượng**: Khi bảo một người *"Đừng nghĩ tới con voi màu hồng"*, não bộ sẽ lập tức hình dung ra con voi màu hồng.
> Các mô hình khuếch tán hình ảnh (Diffusion Models như FLUX, SDXL) **hoàn toàn không hiểu cấu trúc ngữ pháp phủ định** (như `NO...`, `don't...`, `without...`, `never...`).
> Text Encoder (CLIP, T5-XXL) bóc tách toàn bộ các từ danh từ làm token điều hướng hình ảnh.

### ❌ Điều cấm tuyệt đối (Negative Prompt in Positive Prompt):
- **Càng cấm bằng chữ, AI càng vẽ ra!**
- Ví dụ sai:
  - Cấm `NO coffee, NO dark boba, NO brown syrup` $\rightarrow$ AI đọc trúng token `coffee`, `boba`, `brown` và vẽ ngay lớp siro nâu đen ở đáy ly hoặc rải hạt cà phê dưới sàn!
  - Cấm `NO person, NO body, NO torso, NO apron` $\rightarrow$ AI đọc trúng `person`, `body`, `apron` và vẽ ngay một người đứng sau quầy bar!
  - Cấm `NO transparent glass` $\rightarrow$ AI đọc trúng `glass` và vẽ ngay một cái ly thủy tinh!

###  Quy tắc viết đúng (Chỉ thị khẳng định tích cực - Positive Directives):
1. **Chỉ đặc tả những gì muốn thấy, không nhắc tới thứ không muốn thấy**:
   - Muốn ly nước matcha đúng màu: Viết *"vibrant green matcha tea layer and creamy white fresh milk layer with clear ice cubes"*. Tuyệt đối không nhắc tới chữ siro, cà phê hay trân châu.
   - Muốn chỉ thấy tay thao tác, không có người: Viết *"extreme close-up first-person POV shot angled down at the counter, workstation tabletop perspective, hands and the vessel are the sole visible subjects, empty unoccupied background"*.
   - Muốn ly shaker khép kín: Viết *"two hands holding the shaker securely closed in active motion, the shaker is the sole visible vessel"*. Không nhắc đến ly uống nước.

2. **Khóa đặc tả hình học vật thể thay vì để AI tự đoán**:
   - Muốn ly thấp rộng: Dùng danh từ chuyên biệt *"short wide tumbler rocks glass with a broad rim and sturdy base"*. Tránh dùng chữ chung chung `glass` vì AI mặc định là ly trụ cao (Highball/Collins).
   - Muốn góc máy cụ thể: Dùng thông số kỹ thuật nhiếp ảnh như *"shot at a 45-degree angle"*, *"first-person POV"*, *"macro lens"*.

---

## 2. Phân chia rõ 2 tầng Prompt (LLM Directing & Image Rendering)

- **Tầng 1 (LLM Visual Director)**: Nhận dữ liệu công thức, không dịch từng dòng nguyên liệu/gram/ml. LLM phải **tổng hợp thành một đoạn văn miêu tả thị giác ngắn gọn**:
  - Với ảnh thành phẩm: Tả diện mạo ly nước hoàn chỉnh đẹp mắt (màu sắc, đá, bọt, ly).
  - Với ảnh từng bước: Tả hành động pha chế trực tiếp của bàn tay với dụng cụ ở góc nhìn mặt bàn (POV).
- **Tầng 2 (Canonical Style)**: Đặt các thông số nhiếp ảnh, máy ảnh, góc độ và ánh sáng studio lên đầu prompt để cố định chất lượng ảnh chuyên nghiệp.
