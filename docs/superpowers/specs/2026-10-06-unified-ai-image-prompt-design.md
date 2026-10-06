# Thiết kế prompt thống nhất cho ảnh hướng dẫn pha chế

- Ngày: 2026-10-06
- Trạng thái: Đề xuất thiết kế, chờ người dùng duyệt tài liệu
- Phạm vi: Luồng tạo và tạo lại ảnh AI cho từng bước công thức

## 1. Vấn đề

Luồng hiện tại chỉ gửi tên công thức, bước hiện tại và yêu cầu thêm sang Gemini. Nó không gửi nguyên liệu hoặc trạng thái được hình thành từ các bước trước. System prompt hiện yêu cầu Gemini vừa dịch vừa viết mô tả hình ảnh. Trong log thực tế, `Thêm hỗn hợp Matcha` đã bị đổi thành `Pour the vibrant green matcha`: bản dịch bỏ ngữ cảnh và tự thêm mô tả màu sắc.

Log phát triển hiện cũng hiển thị SQL dài từ EF Core. Các service AI đang có thay đổi chưa commit ghi toàn bộ prompt hoặc kết quả AI; thiết kế này cấm các log nội dung đó vì gây nhiễu và có thể lộ dữ liệu.

## 2. Mục tiêu

1. Mỗi ảnh chỉ minh họa đúng hành động của bước hiện tại.
2. Gemini dịch đầy đủ dữ liệu, không tóm tắt, sáng tác hoặc tự chọn phong cách.
3. Context gồm tên công thức, toàn bộ nguyên liệu, tất cả bước trước, bước hiện tại và yêu cầu hình ảnh tùy chọn.
4. Không gửi bước tương lai, ảnh cũ, media library hoặc prompt của draft cũ làm context.
5. Server sở hữu template tạo ảnh FLUX để style ổn định giữa mọi công thức.
6. Không lọc keyword để đoán nguyên liệu, dụng cụ hoặc loại thao tác.
7. Không thêm field dụng cụ vào database hoặc UI. Người dùng mô tả ly, máy hoặc dụng cụ trong ô `Yêu cầu thêm cho ảnh` hiện có.
8. Tắt log SQL mức Information và không log full prompt, API key hoặc raw response.

## 3. Kiến trúc được chọn

```text
Recipe data từ SQLite
  ├─ Tên công thức
  ├─ Toàn bộ nguyên liệu + định lượng + đơn vị
  ├─ Các bước có SortOrder < bước hiện tại
  ├─ Bước hiện tại
  └─ Yêu cầu thêm tùy chọn
          ↓
`AiImagePromptBuilder` gom dữ liệu tiếng Việt theo một cấu trúc cố định.
          ↓
`IAiPromptTranslator` dịch nguyên nội dung sang tiếng Anh:
Gemini là nguồn chính; Cloudflare Llama chỉ dự phòng sau khi hết lượt 429.
          ↓
`AiImagePromptBuilder` ghép bản dịch với khối hướng dẫn FLUX cố định.
          ↓
Cloudflare FLUX.1 Schnell tạo ảnh.
          ↓
`AiImageDraft.PromptSnapshot` lưu nguyên prompt đã gửi cho FLUX.
```

Mọi AI ở bước dịch chỉ được dịch. Chúng không quyết định nội dung hoặc phong cách ảnh. FLUX tạo ảnh từ prompt hoàn chỉnh. Server quyết định cấu trúc prompt và phong cách chung.

## 4. Dữ liệu đầu vào

Khi tạo ảnh cho bước hiện tại, hệ thống dùng chính `currentStep.SortOrder` làm mốc và tải:

- `Recipe.Name`.
- Mọi `RecipeIngredient`, kèm `Ingredient.Name`, `Quantity`, `DefaultUnit`.
- Mọi `RecipeStep` có `SortOrder < currentStep.SortOrder`, sắp xếp tăng dần.
- `currentStep` làm hành động duy nhất cần minh họa. Unique index `(RecipeId, SortOrder)` bảo đảm một công thức không có hai bước cùng thứ tự.
- `userBrief` nếu người dùng nhập.

Không tải bước có `SortOrder > currentStep.SortOrder` cho prompt. Không lấy `PromptSnapshot`, ảnh hoặc draft trước làm context.

Cả `GenerateAiImage` và `RegenerateAiImage` dùng cùng một hàm dựng prompt. Form tạo lại hiện điền sẵn `draft.UserBrief`. Nếu request không có field `userBrief`, dùng lại giá trị cũ; nếu request có field nhưng giá trị rỗng, xóa yêu cầu cũ.

## 5. Prompt nguồn tiếng Việt

Server tạo đúng các section sau:

```text
RECIPE:
Matcha Hân Nè

INGREDIENTS:
- 6 g Matcha
- 50 ml nước nóng
- 30 ml đường nước
- 150 ml sữa

PREVIOUS STEPS — CONTEXT ONLY:
1. Cho Matcha và nước nóng vào cốc, khuấy đều.
2. Cho sữa và đường nước vào ly phục vụ.

CURRENT STEP — ONLY ACTION TO ILLUSTRATE:
3. Thêm hỗn hợp Matcha vào ly.

USER VISUAL REQUIREMENTS:
Sử dụng ly thủy tinh 350 ml, góc nhìn từ trên xuống.
```

Section `USER VISUAL REQUIREMENTS` được bỏ nếu không có nội dung. Không âm thầm cắt tên, nguyên liệu, định lượng, bước hoặc yêu cầu thêm.

## 6. System prompt Gemini

```text
You are a faithful Vietnamese-to-English translator for structured
beverage preparation data.

Translate every provided field faithfully into natural English.

Rules:
- Preserve proper names exactly as written. Translate descriptive recipe
  names while preserving their full meaning.
- Preserve every ingredient, quantity, unit, vessel, preparation state,
  action, and user instruction.
- Preserve the distinction between CONTEXT ONLY and CURRENT ACTION.
- Preserve all section labels and their original order.
- Never summarize, shorten, embellish, reinterpret, or omit information.
- Never infer ingredients, quantities, equipment, vessels, actions,
  colors, textures, or future steps.
- Output only the translated structured content.
```

Trong tài liệu này, “dịch đầy đủ” nghĩa là giữ nguyên mọi thông tin đầu vào. AI không được bỏ chi tiết, rút gọn hoặc tự thêm nội dung.

Adapter kiểm tra phản hồi không rỗng và còn đủ các section bắt buộc; nó không thể tự chứng minh mọi ý nghĩa đã được dịch chính xác. Độ trung thành về nội dung được kiểm tra bằng contract tests với dữ liệu đại diện và benchmark thủ công. Chỉ HTTP 429 mới được thử key tiếp theo. Một vòng là thử mỗi key cấu hình đúng một lần; năm vòng tương đương tối đa `5 × số key` request. Sau khi mọi request đều 429, Cloudflare Llama dịch dự phòng đúng một lần. Lỗi khác dừng ngay và không gọi FLUX.

## 7. Canonical prompt FLUX

Server nối dữ liệu tiếng Anh đã dịch với block cố định:

```text
The CURRENT STEP is the only action to depict.

Use the RECIPE, INGREDIENTS, and PREVIOUS STEPS only to understand
the beverage state immediately before the current action.

Professional café preparation instructional photography.
Clean commercial barista workstation.
Photorealistic ingredients, liquids, vessels, hands, and equipment.
Close, readable composition focused on the active preparation area.
Neutral daylight-balanced studio lighting.
Natural proportions and physically plausible liquid behavior.
Consistent visual appearance across the recipe image series.

Follow USER VISUAL REQUIREMENTS when provided, except when they conflict
with the current action, recipe ingredients, or the strict rules below.
Specialized machines, vessels, or tools may appear only when explicitly
named in the recipe context, current step, or user visual requirements,
and only when relevant to the current action.

Do not repeat previous actions.
Do not depict future actions.
Do not add ingredients absent from the recipe.
No unrelated café props, brand names, logos, text, labels, watermark,
duplicate tools, distorted hands, or physically impossible equipment.
```

Đây là một khung cố định có các phần dữ liệu thay đổi theo công thức, không phải một câu bất biến. Khối hướng dẫn được tham khảo từ tài liệu công khai về cách mô tả chủ thể, bối cảnh, phong cách, ánh sáng và bố cục. Ứng dụng dùng FLUX.1 Schnell, nên phải kiểm tra kết quả trực tiếp trên model này. Hướng dẫn dành cho phiên bản FLUX khác không phải yêu cầu bắt buộc của hệ thống.

## 8. Dụng cụ và yêu cầu nâng cao

Không thêm field `Equipment` vào `RecipeStep`.

Người dùng có thể nhập dụng cụ trong `Yêu cầu thêm cho ảnh`, tối đa 500 ký tự, ví dụ:

```text
Máy espresso thương mại, portafilter đôi, tách sứ trắng 60 ml,
góc máy ngang tầm tách.
```

Nếu không có yêu cầu thêm, prompt không chủ động chỉ định máy hoặc dụng cụ chuyên biệt. Dụng cụ hoặc vật chứa được nêu trong tên công thức, nguyên liệu, bước trước hoặc bước hiện tại được giữ qua bản dịch; FLUX chỉ được dùng chúng khi còn liên quan tới trạng thái và hành động hiện tại.

## 9. Lưu final prompt

`AiImageDraft.PromptSnapshot` lưu nguyên prompt tiếng Anh đã gửi cho FLUX. Giới hạn tăng từ 2.000 lên 8.000 ký tự. Đây là mức lưu mới, không phải bảo đảm rằng mọi công thức bất kỳ đều sẽ vừa.

Cấu trúc bảng SQLite và cấu hình EF phải được cập nhật theo giới hạn mới. Giới hạn 500 ký tự của `userBrief` không đổi.

Nếu prompt hoàn chỉnh vượt 8.000 ký tự, hệ thống báo lỗi trước khi gọi FLUX. Hệ thống không tự cắt dữ liệu.

## 10. Logging

Cấu hình mức log:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
}
```

Các service AI không log:

- Full source/final prompt.
- Bản dịch hoặc raw provider response.
- API key/token.
- Provider error body.

Log được phép:

```text
[DỊCH PROMPT] started step=3 ingredients=4 priorSteps=2
[DỊCH PROMPT] completed provider=Gemini durationMs=1991
[TẠO ẢNH AI] completed provider=Cloudflare durationMs=3400 sizeKb=512
```

Log chỉ được chứa mã recipe/step/draft, số lượng nguyên liệu và bước context, provider, số lượt thử, status code, thời gian xử lý và kích thước ảnh. Không đổi `IAiPromptTranslator` chỉ để truyền dữ liệu log.

## 11. Xử lý lỗi

- Không tìm thấy recipe/step: trả `NotFound`.
- Không có nguyên liệu hoặc instruction rỗng: dừng trước provider và hiển thị lỗi rõ ràng.
- Provider dịch lỗi ngoài 429: dừng; không gọi key khác, provider dự phòng hoặc FLUX.
- Sau `5 × số key` phản hồi 429: gọi Llama đúng một lần.
- Llama lỗi: dừng; không gọi FLUX.
- Bản dịch rỗng hoặc thiếu section bắt buộc: dừng; không gọi FLUX.
- Prompt hoàn chỉnh rỗng hoặc vượt 8.000 ký tự: dừng trước FLUX.
- Caller cancellation: dừng ngay, không retry/fallback.

## 12. Kiểm thử

### Unit/contract tests

1. Source prompt chứa recipe name, toàn bộ ingredients, định lượng/đơn vị, mọi prior step đúng thứ tự, current step và user brief.
2. Source prompt không chứa future steps.
3. Current step được đánh dấu là hành động duy nhất cần minh họa.
4. Gemini request chứa system prompt dịch đầy đủ; parser không cắt text parts.
5. Final prompt chứa canonical style đúng một lần.
6. Regenerate dùng lại `draft.UserBrief` khi field không được gửi; field được gửi rỗng thì xóa yêu cầu cũ.
7. Final prompt dài hơn 2.000 nhưng không quá 8.000 được lưu nguyên vẹn.
8. Prompt vượt 8.000 bị từ chối trước khi gọi FLUX.
9. Log không chứa prompt, translated output, raw response, key hoặc token.
10. Toàn bộ regression tests về `5 × số key`, 429 và fallback tiếp tục pass.

### Benchmark thủ công trên FLUX.1 Schnell

Dùng 12–15 bước thật; mỗi bước là một case. Bộ case phải gồm cân/đong, khuấy, đánh matcha, espresso, hấp sữa, xay, lắc, rót, tạo lớp, topping, chuyển hỗn hợp và bước mơ hồ. Mỗi case tạo ba ảnh và chấm:

- Đúng hành động hiện tại.
- Đúng trạng thái từ bước trước.
- Không thêm/bỏ nguyên liệu.
- Dụng cụ đúng yêu cầu thêm.
- Không trộn bước trước hoặc bước tương lai.
- Style nhất quán.
- Tay, ly, chất lỏng và thiết bị hợp lý.

Một case đạt khi ít nhất hai trong ba ảnh đúng hành động hiện tại, không thêm nguyên liệu và không đưa bước tương lai vào ảnh. Toàn bộ benchmark đạt khi mọi case đạt và ít nhất 80% số ảnh đáp ứng đủ bảy tiêu chí. Mỗi vòng benchmark chỉ thay một khối prompt để biết thay đổi nào tạo khác biệt.

## 13. Ngoài phạm vi

- Không thêm trường dụng cụ vào `RecipeStep`.
- Không xây danh mục máy, ly hoặc dụng cụ.
- Không lọc keyword hoặc viết rule theo loại đồ uống.
- Không thêm style selector vào UI.
- Không đổi model FLUX.
- Không dùng thêm một lượt AI để tóm tắt trạng thái.
- Không gửi bước tương lai làm context.
