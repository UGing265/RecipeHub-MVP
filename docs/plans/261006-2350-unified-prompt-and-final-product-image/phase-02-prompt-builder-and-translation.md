# Phase 2: Prompt Builder & AI Translation Faithful Contract

## 1. Mục tiêu
- Viết lại `AiImagePromptBuilder` để gom dữ liệu có cấu trúc (toàn bộ nguyên liệu + các bước trước + bước hiện tại / toàn bộ quy trình).
- Định nghĩa hằng số System Instruction trung thành dùng chung cho cả Gemini và Llama fallback.
- Tăng `generationConfig.maxOutputTokens` của Gemini lên 4.096 và kiểm tra các nhãn section bắt buộc sau khi dịch.
- Tách 2 canonical FLUX prompt blocks: Step Instruction và Final Product Hero.

## 2. Thay đổi chi tiết

### 2.1. Hằng số System Instruction dùng chung
- **`src/RecipeCard.Web/Services/AiPromptTranslationConstants.cs`**:
  Tạo class hằng số dùng chung:
  ```csharp
  public static class AiPromptTranslationConstants
  {
      public const string FaithfulSystemInstruction =
          """
          You are a faithful Vietnamese-to-English translator for structured beverage preparation data.

          Translate every provided field faithfully into natural English.

          Rules:
          - Preserve proper names exactly as written. Translate descriptive recipe names while preserving their full meaning.
          - Preserve every ingredient, quantity, unit, vessel, preparation state, action, and user instruction.
          - Preserve every action explicitly written in CURRENT STEP.
          - Never add, remove, replace, merge, or reinterpret actions.
          - Preserve the distinction between CONTEXT ONLY and target content.
          - Preserve all section labels and their original order.
          - Never summarize, shorten, embellish, or omit information.
          - Never infer ingredients, quantities, equipment, vessels, actions, colors, textures, people, or future steps.
          - If information remains unspecified, translate it neutrally.
          - Output only the translated structured content.
          """;

      public const int MaxOutputTokens = 4096;
      public const int MaxFinalPromptLength = 8000;
  }
  ```

### 2.2. Cập nhật Gemini and Cloudflare Translator
- **`src/RecipeCard.Web/Services/GeminiRoundRobinPromptTranslator.cs`**:
  - Đổi system instruction sang `AiPromptTranslationConstants.FaithfulSystemInstruction`.
  - Đổi `maxOutputTokens` trong `generationConfig` từ `150` sang `AiPromptTranslationConstants.MaxOutputTokens` (4.096).
  - Sau khi nhận bản dịch text từ Gemini, kiểm tra có chứa các nhãn section bắt buộc (ví dụ `TARGET:`, `RECIPE:`) bằng chuỗi không phân biệt hoa thường. Nếu thiếu, coi như bản dịch lỗi và throw `InvalidOperationException`.
- **`src/RecipeCard.Web/Services/CloudflareWorkersAiPromptTranslator.cs`**:
  - Đổi system prompt của Llama sang dùng cùng `AiPromptTranslationConstants.FaithfulSystemInstruction`.
  - Tăng `max_tokens` của Llama lên 4.096 để khớp khả năng xuất dữ liệu đầy đủ.

### 2.3. Cải tiến AiImagePromptBuilder
- **`src/RecipeCard.Web/Services/AiImagePromptBuilder.cs`**:
  - Bổ sung phương thức dựng source prompt tiếng Việt cho ảnh bước:
    ```csharp
    public string BuildStepSourcePrompt(
        string recipeName,
        IEnumerable<(string Name, decimal Quantity, string Unit)> ingredients,
        IEnumerable<(int SortOrder, string Instruction)> priorSteps,
        int currentSortOrder,
        string currentInstruction,
        string? userBrief)
    ```
  - Bổ sung phương thức dựng source prompt tiếng Việt cho ảnh thành phẩm:
    ```csharp
    public string BuildFinalProductSourcePrompt(
        string recipeName,
        IEnumerable<(string Name, decimal Quantity, string Unit)> ingredients,
        IEnumerable<(int SortOrder, string Instruction)> allSteps,
        string? generalNote,
        string? userBrief)
    ```
  - Bổ sung phương thức ghép canonical FLUX style theo target:
    ```csharp
    public string AttachCanonicalStyle(AiDraftTargetKind targetKind, string translatedEnglishPrompt)
    ```
    - Với `StepInstruction`: Nối khối quy tắc bước (chỉ hành động hiện tại, chỉ bàn tay/cẳng tay, không mặt/người/gợi dục, không đạo cụ/máy móc bịa thêm).
    - Với `FinalProduct`: Nối khối quy tắc thành phẩm (chỉ món hoàn chỉnh, nền quầy bar sạch, không bàn tay, không chuỗi thao tác, không người/đạo cụ thừa).
  - Kiểm tra độ dài prompt hoàn chỉnh: nếu vượt quá `8.000` ký tự, quăng lỗi `InvalidOperationException` báo rõ công thức quá dài để người dùng rút gọn ghi chú, không âm thầm cắt bớt dữ liệu.

## 3. Tiêu chí nghiệm thu Phase 2
- Unit test kiểm chứng: source prompt chứa đầy đủ nguyên liệu và bước trước theo đúng thứ tự; không chứa bước tương lai.
- Unit test kiểm chứng: Gemini và Llama payload chứa cùng một chuỗi `FaithfulSystemInstruction` và `maxOutputTokens = 4096`.
- Unit test kiểm chứng: `AttachCanonicalStyle` gắn đúng block quy tắc theo enum `AiDraftTargetKind`.
- Unit test kiểm chứng: prompt hoàn chỉnh > 8.000 ký tự bị chặn và báo lỗi rõ ràng.
