# Phase 1: Dữ liệu, Migration và Logging

## 1. Mục tiêu
- Cập nhật mô hình dữ liệu SQLite cho ảnh thành phẩm, preset tỷ lệ và phân loại draft.
- Mở rộng cột `PromptSnapshot` từ 2.000 lên 8.000 ký tự.
- Cấu hình tắt log SQL EF Core mức Information để giảm nhiễu màn hình console.

## 2. Thay đổi chi tiết

### 2.1. Enum và Model
- **`src/RecipeCard.Web/Models/AspectRatioPreset.cs`**:
  Tạo mới enum:
  - `Square1x1` = 0 (1024×1024)
  - `StandardLandscape4x3` = 1 (1024×768)
  - `WideLandscape16x9` = 2 (1280×720)
  - `Portrait4x5` = 3 (768×960)
- **`src/RecipeCard.Web/Models/AiDraftTargetKind.cs`**:
  Tạo mới enum:
  - `StepInstruction` = 0
  - `FinalProduct` = 1
- **`src/RecipeCard.Web/Models/Recipe.cs`**:
  - Thêm `public int? FinalMediaAssetId { get; set; }`
  - Thêm `public MediaAsset? FinalMediaAsset { get; set; }`
  - Thêm `public AspectRatioPreset FinalImageAspectRatioPreset { get; set; } = AspectRatioPreset.WideLandscape16x9;`
- **`src/RecipeCard.Web/Models/RecipeStep.cs`**:
  - Thêm `public AspectRatioPreset ImageAspectRatioPreset { get; set; } = AspectRatioPreset.Square1x1;`
- **`src/RecipeCard.Web/Models/AiImageDraft.cs`**:
  - Thêm `public int RecipeId { get; set; }`
  - Thêm `public Recipe Recipe { get; set; } = null!;`
  - Đổi `public int? RecipeStepId { get; set; }` (từ `int` sang `int?`)
  - Thêm `public AiDraftTargetKind TargetKind { get; set; } = AiDraftTargetKind.StepInstruction;`
  - Thêm `public AspectRatioPreset AspectRatioPreset { get; set; } = AspectRatioPreset.Square1x1;`
  - Cập nhật property `PromptSnapshot`: max length 8.000 (thay vì 2.000).

### 2.2. Entity Framework DbContext & Migration
- **`src/RecipeCard.Web/Data/RecipeDbContext.cs`**:
  - Cấu hình quan hệ `Recipe.FinalMediaAsset` (FK: `FinalMediaAssetId`, `DeleteBehavior.Restrict`).
  - Cấu hình quan hệ `AiImageDraft.Recipe` (FK: `RecipeId`, `DeleteBehavior.Cascade`).
  - Cấu hình quan hệ `AiImageDraft.RecipeStep` (FK: `RecipeStepId`, `DeleteBehavior.Cascade`, nullable).
  - Cấu hình `builder.Entity<AiImageDraft>().Property(x => x.PromptSnapshot).HasMaxLength(8000);`.
- **Tạo migration SQLite**:
  - Chạy lệnh `dotnet ef migrations add AddFinalProductImageAndDraftPresets --project src/RecipeCard.Web`.
  - Trong phương thức `Up()` của migration:
    - Viết câu lệnh SQL backfill: gán `RecipeId` cho các bản ghi `AiImageDrafts` hiện có thông qua `RecipeSteps.RecipeId`.
    - Gán mặc định `TargetKind = 0` (StepInstruction) và `AspectRatioPreset = 0` (Square1x1) cho các draft hiện có.
    - Áp dụng migration vào database cục bộ `recipe-card.db`.

### 2.3. Cấu hình Logging
- **`src/RecipeCard.Web/appsettings.json`**:
  Thêm category logging để tắt SQL command thừa:
  ```json
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  }
  ```

## 3. Tiêu chí nghiệm thu Phase 1
- `dotnet ef database update` chạy thành công không mất dữ liệu hiện có.
- Khởi động app: console không còn in các log `Executed DbCommand (1ms) SELECT ...`.
- Các entity lưu trữ được `PromptSnapshot` dài đến 8.000 ký tự mà không bị cắt hoặc lỗi DbUpdateException.
