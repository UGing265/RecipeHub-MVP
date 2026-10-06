# Thiết kế prompt thống nhất và ảnh thành phẩm công thức

- Ngày: 2026-10-06
- Trạng thái: Đề xuất thiết kế, chờ người dùng duyệt tài liệu cập nhật
- Phạm vi: Ảnh AI cho từng bước, ảnh thành phẩm cấp công thức, tỷ lệ ảnh và logging

## 1. Vấn đề

Luồng hiện tại chỉ gửi tên công thức, bước hiện tại và yêu cầu thêm sang Gemini. Nó không gửi nguyên liệu hoặc trạng thái được hình thành từ các bước trước. System prompt hiện yêu cầu Gemini vừa dịch vừa viết mô tả hình ảnh. Trong log thực tế, `Thêm hỗn hợp Matcha` đã bị đổi thành `Pour the vibrant green matcha`: bản dịch bỏ ngữ cảnh và tự thêm mô tả màu sắc.

Log phát triển hiện cũng hiển thị SQL dài từ EF Core. Các service AI đang có thay đổi chưa commit ghi toàn bộ prompt hoặc kết quả AI; thiết kế này cấm các log nội dung đó vì gây nhiễu và có thể lộ dữ liệu.

## 2. Mục tiêu

1. Ảnh bước chỉ minh họa các hành động được nêu trong bước hiện tại; không thêm, thay hoặc bỏ hành động.
2. Ảnh thành phẩm là hero sạch của món hoàn chỉnh, không hiển thị chuỗi thao tác, người hoặc đạo cụ thừa.
3. Gemini và Llama dịch đầy đủ dữ liệu, không tóm tắt, sáng tác hoặc tự chọn phong cách.
4. Context ảnh bước gồm tên công thức, toàn bộ nguyên liệu, tất cả bước trước, bước hiện tại và yêu cầu hình ảnh tùy chọn.
5. Context ảnh thành phẩm gồm tên, toàn bộ nguyên liệu, toàn bộ quy trình và yêu cầu hình ảnh tùy chọn.
6. Server sở hữu hai template FLUX cố định: step instructional và final-product hero.
7. Không lọc keyword để đoán nguyên liệu, dụng cụ hoặc loại thao tác.
8. Không thêm field dụng cụ. Người dùng mô tả ly, máy hoặc dụng cụ trong ô `Yêu cầu thêm cho ảnh`.
9. Hỗ trợ upload, chọn thư viện và AI cho ảnh thành phẩm.
10. Hiển thị ảnh thành phẩm tại Edit, Index, Preview và PDF.
11. Hỗ trợ preset kích thước dễ hiểu thay vì bắt người dùng nhập pixel.
12. Tắt log SQL mức Information và không log full prompt, API key hoặc raw response.

## 3. Kiến trúc được chọn

```text
Recipe data từ SQLite
  ├─ Tên công thức
  ├─ Toàn bộ nguyên liệu + định lượng + đơn vị
  ├─ Step target: bước trước + bước hiện tại
  ├─ Final target: toàn bộ quy trình
  └─ Yêu cầu thêm tùy chọn
          ↓
AiImagePromptBuilder tạo source prompt có cấu trúc theo target
          ↓
IAiPromptTranslator dịch nguyên nội dung sang tiếng Anh:
Gemini là nguồn chính; Cloudflare Llama chỉ dự phòng sau khi hết lượt 429
          ↓
AiImagePromptBuilder ghép canonical prompt theo target:
StepInstruction hoặc FinalProductHero
          ↓
Cloudflare FLUX.2 Klein 4B tạo ảnh đúng width/height preset
          ↓
AiImageDraft lưu prompt, target và preset để người dùng duyệt
          ↓
MediaAsset được chấp nhận:
RecipeStep.MediaAssetId hoặc Recipe.FinalMediaAssetId
```

Mọi AI ở bước dịch chỉ được dịch. Chúng không quyết định nội dung hoặc phong cách ảnh. FLUX tạo ảnh từ prompt hoàn chỉnh. Server quyết định cấu trúc, target và phong cách chung.

## 4. Dữ liệu đầu vào

### 4.1. Ảnh bước

Hệ thống dùng `currentStep.SortOrder` làm mốc và tải:

- `Recipe.Name`.
- Mọi `RecipeIngredient`, kèm `Ingredient.Name`, `Quantity`, `DefaultUnit`.
- Mọi `RecipeStep` có `SortOrder < currentStep.SortOrder`, sắp xếp tăng dần.
- `currentStep`; mọi hành động được ghi trong bước này đều phải được giữ.
- `userBrief` nếu người dùng nhập.

Không tải bước tương lai, `PromptSnapshot`, ảnh hoặc draft trước làm context.

### 4.2. Ảnh thành phẩm

Hệ thống tải tên công thức, toàn bộ nguyên liệu, toàn bộ quy trình theo `SortOrder`, `GeneralNote` và `userBrief`. Toàn bộ quy trình chỉ mô tả trạng thái thành phẩm; FLUX không được dựng lại chuỗi thao tác.

Generate và Regenerate theo cùng một builder cho từng target. Form tạo lại điền sẵn `draft.UserBrief`. Request thiếu field `userBrief` thì dùng lại giá trị cũ; request có field rỗng thì chủ động xóa yêu cầu cũ.

## 5. Prompt nguồn tiếng Việt

Server tạo source prompt có section cố định. Ảnh bước dùng:

```text
TARGET: STEP_INSTRUCTION

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

CURRENT STEP — ONLY ACTIONS TO ILLUSTRATE:
3. Thêm hỗn hợp Matcha vào ly rồi khuấy nhẹ.

USER VISUAL REQUIREMENTS:
Sử dụng ly thủy tinh 350 ml, góc nhìn từ trên xuống.
```

Ảnh thành phẩm dùng:

```text
TARGET: FINAL_PRODUCT

RECIPE:
Matcha Hân Nè

INGREDIENTS:
[toàn bộ nguyên liệu]

FULL PROCESS — FINAL STATE CONTEXT ONLY:
[toàn bộ quy trình theo thứ tự]

GENERAL NOTE:
[ghi chú chung nếu có]

USER VISUAL REQUIREMENTS:
[yêu cầu tùy chọn]
```

Section tùy chọn được bỏ khi không có nội dung. Không âm thầm cắt tên, nguyên liệu, định lượng, bước hoặc yêu cầu thêm.

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
- Preserve every action explicitly written in CURRENT STEP.
- Never add, remove, replace, merge, or reinterpret actions.
- Preserve the distinction between CONTEXT ONLY and target content.
- Preserve all section labels and their original order.
- Never summarize, shorten, embellish, or omit information.
- Never infer ingredients, quantities, equipment, vessels, actions,
  colors, textures, people, or future steps.
- If information remains unspecified, translate it neutrally.
- Output only the translated structured content.
```

Cả hai adapter (Gemini và Llama) dùng chung một chuỗi system instruction được định nghĩa tập trung ở một hằng số chung (ví dụ `AiPromptTranslationConstants.SystemInstruction`), bảo đảm không lệch nhau. “Dịch đầy đủ” nghĩa là giữ nguyên mọi thông tin đầu vào, không bỏ, rút gọn hoặc tự thêm. Đây là quy tắc ngôn ngữ, không phải code lọc keyword.

`generationConfig.maxOutputTokens` của Gemini tăng từ 150 lên 4.096 để full-context không bị cắt. Adapter kiểm tra phản hồi không rỗng và chứa đủ các nhãn section bắt buộc (`TARGET:`, `RECIPE:`, v.v.) bằng kiểm tra nhãn chuỗi; độ trung thành ngữ nghĩa được bảo đảm bằng contract tests và benchmark. Llama được gọi đúng một lần duy nhất sau khi toàn bộ `5 × số key` lượt Gemini đều nhận HTTP 429; nếu Llama cũng thất bại (bất kỳ lỗi gì), hệ thống dừng ngay và không gọi FLUX. Lỗi non-429 ở Gemini dừng ngay lập tức.

## 7. Canonical prompts FLUX

Server nối bản dịch với đúng một block theo target.

### 7.1. Ảnh bước

```text
Depict only the actions explicitly stated in CURRENT STEP.
Do not add, replace, combine, or omit preparation actions.

Use RECIPE, INGREDIENTS, and PREVIOUS STEPS only to understand the
beverage state immediately before CURRENT STEP.
The target vessel, contents, ingredient color, texture, and physical
form must follow that context. If details remain unknown, use a simple
neutral unbranded vessel and minimal ordinary tool; do not invent details.

Show only hands and forearms required for the stated actions.
No face, full person, body, customer, model, bystander, sexualized pose,
revealing clothing, or unrelated human figure.

Professional café instructional photography, clean commercial barista
workstation, close readable composition, neutral daylight-balanced
lighting, natural proportions, realistic liquid behavior, and consistent
visual appearance across the recipe series.

USER VISUAL REQUIREMENTS apply only when they do not conflict with recipe
facts or these strict rules. No prior/future actions, extra ingredients,
garnishes, colors, specialized machines, unrelated props, brand names,
logos, text, labels, watermarks, duplicate hands/tools, extra fingers,
malformed vessels, background people, or impossible equipment.
```

### 7.2. Ảnh thành phẩm

```text
Create one clean hero product photograph of the completed recipe.
Use RECIPE, INGREDIENTS, FULL PROCESS, and GENERAL NOTE only to determine
the final beverage, vessel, layers, texture, color, ice, foam, and garnish.
Do not depict preparation actions, hands, people, machines, ingredient
containers, or unrelated café props.

The finished beverage is the only subject. Professional café product
photography, clean neutral barista counter, balanced composition,
daylight-neutral studio lighting, realistic materials and liquid behavior,
and consistent visual appearance with the recipe step series.

USER VISUAL REQUIREMENTS apply only when they do not conflict with recipe
facts or these strict rules. No extra ingredients, invented toppings,
brand names, logos, text, labels, watermarks, duplicate vessels,
malformed glassware, background people, or sexualized content.
```

Hai block là quy tắc tổng quát, không chứa `if/Contains` theo từ khóa và không hard-code từng loại đồ uống.

## 8. Dụng cụ và yêu cầu nâng cao

Không thêm field `Equipment` vào `RecipeStep`.

Người dùng có thể nhập dụng cụ trong `Yêu cầu thêm cho ảnh`, tối đa 500 ký tự, ví dụ:

```text
Máy espresso thương mại, portafilter đôi, tách sứ trắng 60 ml,
góc máy ngang tầm tách.
```

Nếu không có yêu cầu thêm, prompt không chủ động chỉ định máy hoặc dụng cụ chuyên biệt. Dụng cụ hoặc vật chứa được nêu trong tên công thức, nguyên liệu, bước trước hoặc bước hiện tại được giữ qua bản dịch; FLUX chỉ được dùng chúng khi còn liên quan tới trạng thái và hành động hiện tại.

## 9. FLUX.2 Klein 4B và preset kích thước

Model ảnh duy nhất trong scope:

```text
@cf/black-forest-labs/flux-2-klein-4b
```

Đây là model native Workers AI, dùng quota Neurons của Cloudflare. Request đổi từ JSON FLUX.1 sang `multipart/form-data` gồm `prompt`, `width`, `height` và `seed` tùy chọn. Không gửi `steps` vì Klein 4B cố định bốn bước. Response base64 được decode, kiểm tra magic bytes rồi đi qua storage hiện có.

`IAiImageGenerator.GenerateAsync` đổi signature thành nhận `AiImageGenerationRequest` thay vì chuỗi prompt đơn lẻ:

```text
AiImageGenerationRequest
  Prompt
  AspectRatioPreset (enum trong Models/Services)
  Width
  Height
  Seed (optional)
```

Request gửi tới Cloudflare Workers AI dùng multipart/form-data với các field: `prompt`, `width`, `height`, và `seed` nếu có; không gửi field `steps` vì Klein 4B không chấp nhận.

UI chỉ hiển thị preset:

| Nhãn | Tỷ lệ | Kích thước |
|---|---:|---:|
| Vuông | 1:1 | 1024×1024 |
| Ngang chuẩn | 4:3 | 1024×768 |
| Ngang rộng | 16:9 | 1280×720 |
| Dọc | 4:5 | 768×960 |

Không cho nhập pixel tùy ý. Preset áp dụng cho ảnh bước và ảnh thành phẩm. Ảnh AI được tạo native theo kích thước chọn, không tạo vuông rồi crop. Với ảnh upload hoặc thư viện có tỷ lệ khác, giữ nguyên bytes và dùng preset làm khung crop không phá hủy tại Edit, Index, Preview và PDF.

## 10. Ảnh thành phẩm và vòng đời media

### 10.1. Quan hệ dữ liệu

- `Recipe.FinalMediaAssetId` là FK nullable tới `MediaAsset`.
- `Recipe.FinalImageAspectRatioPreset` lưu preset đang hiển thị.
- `RecipeStep.ImageAspectRatioPreset` lưu preset của ảnh bước.
- `AiImageDraft.RecipeId` là FK bắt buộc.
- `AiImageDraft.RecipeStepId` chuyển thành nullable.
- `AiImageDraft.TargetKind` (enum: `StepInstruction`, `FinalProduct`).
- `AiImageDraft.AspectRatioPreset` lưu lựa chọn lúc generate.

Migration backfill: mọi draft hiện có (kể cả Generated, Accepted, Expired) được gán `RecipeId` suy ra từ `RecipeStep.RecipeId`, và `TargetKind = StepInstruction`. Ràng buộc hợp lệ (draft step phải có `RecipeStepId`, draft final-product không có `RecipeStepId`) được kiểm tra ở application layer khi tạo/nhận draft.
### 10.2. Nguồn ảnh thành phẩm

Trang Edit có khu vực `Ảnh thành phẩm` hỗ trợ:

1. Upload ảnh thật qua validation/storage hiện có.
2. Chọn một `MediaAsset` từ thư viện.
3. Tạo ảnh AI thành phẩm rồi Accept/Discard.

Mỗi recipe có tối đa một ảnh thành phẩm active. Thay ảnh chỉ đổi liên kết; không tự xóa `MediaAsset` cũ vì asset có thể đang được dùng ở nơi khác. Luồng xóa media phải kiểm tra cả `Recipe.FinalMediaAssetId` và `RecipeStep.MediaAssetId`.

Accept draft chạy transaction: xác nhận draft còn hợp lệ, tạo hoặc liên kết `MediaAsset`, gán đúng target FK và preset, đánh dấu draft accepted. Discard và cleanup xóa file tạm theo chính sách hiện có. TargetKind ngăn draft ảnh thành phẩm ghi nhầm vào step và ngược lại.

### 10.3. Hiển thị
- Edit: ảnh lớn, source badge, preset và thao tác thay/xóa.
- Index: thumbnail theo preset.
- Preview: hero trước nội dung recipe.
- PDF: cấp `RecipePdfModel.HeroImageBytes/HeroImageMimeType`; QuestPDF render ảnh vào khung kích thước theo preset với tỷ lệ cố định, không cắt byte gốc. Nếu không có ảnh thì layout hiện tại tiếp tục hoạt động không hero.
## 11. Lưu final prompt
`AiImageDraft.PromptSnapshot` lưu nguyên prompt tiếng Anh đã gửi FLUX. Giới hạn 8.000 ký tự áp dụng cho prompt tiếng Anh HOÀN CHỈNH — tức là sau khi ghép bản dịch với canonical FLUX block; `userBrief` vẫn tối đa 500 ký tự. Prompt hoàn chỉnh vượt 8.000 bị từ chối trước provider, không bị cắt. `AiImagePromptBuilder` kiểm tra trước độ dài prompt tiếng Việt để tránh dịch lãng phí nếu ước tính vượt ngưỡng.
## 12. Logging

Cấu hình:

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

Không log source/final prompt, bản dịch, raw response, key/token hoặc provider error body. Log chỉ chứa target kind, mã recipe/step/draft, preset, kích thước, provider, lượt thử, numeric status code, thời gian và byte size. Metadata được log tại handler/generator; không đổi translator contract chỉ để log.

## 13. Xử lý lỗi

- Không tìm thấy recipe/step/draft/media: trả `NotFound`.
- Recipe không có nguyên liệu hoặc step target có instruction rỗng: dừng trước provider.
- Provider dịch lỗi ngoài 429: dừng; không gọi key khác, fallback hoặc FLUX.
- Sau `5 × số key` phản hồi 429: gọi Llama đúng một lần.
- Bản dịch rỗng/thiếu section hoặc prompt vượt 8.000: dừng trước FLUX.
- FLUX trả base64/image sai: không tạo draft/media; thông báo generic.
- Kích thước không thuộc preset: từ chối trước FLUX.
- Caller cancellation: dừng ngay, không retry/fallback.
- Accept draft sai target, hết hạn hoặc đã xử lý: từ chối; không thay active media.

## 14. Kiểm thử

### 14.1. Prompt và translator

1. Step source chứa recipe, toàn bộ ingredients, prior steps đúng thứ tự, current step và user brief; không chứa future steps.
2. Mọi hành động trong current step được giữ; không keyword filtering.
3. Final-product source chứa toàn bộ recipe/process nhưng canonical prompt chỉ yêu cầu thành phẩm.
4. Gemini và Llama dùng cùng faithful-translation instruction.
5. `maxOutputTokens = 4096`; parser nối đủ text parts.
6. Canonical block đúng target và xuất hiện đúng một lần.
7. Regenerate dùng lại brief khi field absent; field rỗng thì xóa.
8. Prompt 2.001–8.000 ký tự được lưu nguyên; trên 8.000 bị từ chối.
9. Regression tests `5 × số key`, 429, timeout và fallback tiếp tục pass.

### 14.2. Generator và preset

1. Klein 4B request là multipart, đúng model ID, width/height/seed, không có `steps`.
2. Base64 hợp lệ được decode; dữ liệu sai không tạo draft.
3. Bốn preset map đúng kích thước và giá trị ngoài enum bị từ chối.
4. Step và final-product đều truyền preset vào generator.
5. Log không chứa prompt, output, raw response hoặc secret.

### 14.3. Media và UI

1. Migration backfill draft cũ và giữ toàn vẹn target.
2. Upload/library/AI đều có thể gán final image.
3. Accept draft gán đúng `RecipeStep` hoặc `Recipe` trong transaction.
4. Replace/unlink không xóa asset dùng chung.
5. Media đang được recipe hoặc step dùng không bị xóa sai.
6. Edit, Index, Preview và PDF dùng final image; null hero có fallback sạch.
7. Crop frame theo preset nhất quán cho upload/library; AI có native dimensions.

## 15. Benchmark FLUX.2 Klein 4B

Dùng 12–15 bước thật và 4–6 công thức hoàn chỉnh. Mỗi case tạo ba ảnh với preset đại diện.

Ảnh bước chấm: đúng hành động, trạng thái trước, nguyên liệu, dụng cụ, không trộn bước, style và vật lý. Ảnh thành phẩm chấm: đúng món cuối, ly/lớp/topping, không có thao tác/tay/người/đạo cụ thừa, style hero và tỷ lệ.

Case đạt khi ít nhất hai trong ba ảnh đúng target, không thêm nguyên liệu và không thêm người/bước tương lai. Benchmark đạt khi mọi case đạt và ít nhất 80% ảnh đáp ứng toàn bộ tiêu chí. Mỗi vòng chỉ thay một block prompt.

## 16. Giới hạn và ngoài phạm vi

- Prompt giảm sai lệch nhưng không bảo đảm ảnh đúng 100%; người dùng vẫn duyệt draft thủ công.
- Không thêm AI thứ hai để đọc/chấm ảnh.
- Không thêm field hoặc danh mục dụng cụ.
- Không lọc keyword hoặc viết rule theo loại đồ uống.
- Không thêm style selector; chỉ có preset tỷ lệ/kích thước.
- Không hỗ trợ model third-party AI Gateway trong plan này.
- Không dùng thêm lượt AI để tóm tắt context.
- Không gửi bước tương lai vào ảnh bước.
- Không cho nhập width/height tùy ý.
