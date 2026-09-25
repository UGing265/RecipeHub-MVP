# Tầng dịch prompt Việt → Anh cho minh họa AI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trước mỗi lần tạo hoặc tạo lại minh họa AI, dịch prompt SOP tiếng Việt thành tiếng Anh bằng Cloudflare Workers AI rồi mới gửi prompt đã dịch vào FLUX.1 Schnell.

**Architecture:** `AiImagePromptBuilder` tách phần ngữ cảnh SOP tiếng Việt và guideline ảnh tiếng Anh cố định. `CloudflareWorkersAiPromptTranslator` nhận phần tiếng Việt, gọi `@cf/meta/m2m100-1.2b` với `{ text, source_lang: "vi", target_lang: "en" }`, đọc `result.translated_text`, sau đó editor ghép guideline và gọi `IAiImageGenerator` như hiện tại. Bản nháp tiếp tục lưu chính xác English prompt đã gửi FLUX trong `AiImageDraft.PromptSnapshot`.

**Tech Stack:** ASP.NET Core Razor Pages, .NET 10, `HttpClientFactory`, Cloudflare Workers AI REST API, xUnit.

---

## Scope đã chốt

- Áp dụng cho cả `GenerateAiImage` và `RegenerateAiImage`.
- Phase này **luôn** gọi translator với `source_lang: "vi"`; prompt tiếng Anh hoàn toàn hoặc pha Việt–Anh chưa được phát hiện/bỏ qua.
- Không đổi UI, schema SQLite, candidate lifecycle, Cloudinary upload flow, hay FLUX image payload parser.
- Không thêm API key: tái sử dụng `Cloudflare:AccountId` và `Cloudflare:ApiToken` đã có.
- Nếu dịch thất bại/rỗng/sai schema: hủy tạo ảnh, hiển thị lỗi dịch rõ ràng, không dùng prompt tiếng Việt làm fallback cho FLUX.
- Phase tiếp theo, ngoài plan này: language detection và bypass dịch khi chắc chắn tiếng Anh.

## Cloudflare contract

Nguồn: [Workers AI M2M100-1.2B](https://developers.cloudflare.com/workers-ai/models/m2m100-1.2b/).

```http
POST https://api.cloudflare.com/client/v4/accounts/{AccountId}/ai/run/@cf/meta/m2m100-1.2b
Authorization: Bearer {ApiToken}
Content-Type: application/json

{
  "text": "Công thức đồ uống: Matcha sữa dừa. Bước 2: Đong sữa dừa vào ly có đá.",
  "source_lang": "vi",
  "target_lang": "en"
}
```

```json
{
  "result": {
    "translated_text": "Beverage recipe: Coconut milk matcha. Step 2: Pour coconut milk into a glass with ice."
  }
}
```

The synchronous request above is documented with `result.translated_text`. The adapter also accepts `result[0].translated_text`, an alternate documented result envelope, so a response-shape variation does not silently feed Vietnamese source text to FLUX.

## File map

| Action | Path | Responsibility |
|---|---|---|
| Modify | `src/RecipeCard.Web/Services/CloudflareOptions.cs` | Add configurable translation-model ID; retain existing account/token validation. |
| Create | `src/RecipeCard.Web/Services/IAiPromptTranslator.cs` | Stable async boundary between editor and Cloudflare translation transport. |
| Create | `src/RecipeCard.Web/Services/CloudflareWorkersAiPromptTranslator.cs` | Server-only M2M100 REST adapter, response parsing, safe error mapping. |
| Modify | `src/RecipeCard.Web/Services/AiImagePromptBuilder.cs` | Create Vietnamese source context separately; append English style only after translation. |
| Modify | `src/RecipeCard.Web/Program.cs` | Register typed `HttpClient` for `IAiPromptTranslator`. |
| Modify | `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs` | Inject translator and use one shared translated-prompt method in generation and regeneration. |
| Modify | `tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs` | Inject fake translator; prove generator receives English final prompt and failures prevent draft creation. |
| Create | `tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs` | Verify outbound REST contract and successful/error response handling without a real Cloudflare call. |
| Modify | `src/RecipeCard.Web/appsettings.json` | Add non-secret `Cloudflare:TranslationModel` template value only if this configuration section is already present. |

## Implementation tasks

### Task 1: Define translation configuration and contract

**Files:**
- Modify: `src/RecipeCard.Web/Services/CloudflareOptions.cs`
- Create: `src/RecipeCard.Web/Services/IAiPromptTranslator.cs`
- Test: `tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs`

- [x] **Step 1: Write the API contract tests before transport code.**

Create tests that compile against:

```csharp
public interface IAiPromptTranslator
{
    Task<string> TranslateVietnameseToEnglishAsync(
        string vietnamesePrompt,
        CancellationToken cancellationToken = default);
}
```

Cover invalid blank input as `ArgumentException`; the response tests follow in Task 2.

- [x] **Step 2: Run the new contract test and confirm it fails because the service does not exist.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiPromptTranslatorTests"
```

Expected: compilation failure naming `IAiPromptTranslator` or `CloudflareWorkersAiPromptTranslator`.

- [x] **Step 3: Add minimal shared configuration and abstraction.**

In `CloudflareOptions`, retain `Model` for FLUX and add:

```csharp
public string TranslationModel { get; set; } = "@cf/meta/m2m100-1.2b";
```

Create the interface above. Do not duplicate `AccountId` or `ApiToken` in a second options type.

- [x] **Step 4: Re-run the focused test.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiPromptTranslatorTests"
```

Expected: test project compiles; transport-specific assertions remain failing/unimplemented until Task 2.

- [x] **Step 5: Commit the contract.**

```bash
git add src/RecipeCard.Web/Services/CloudflareOptions.cs src/RecipeCard.Web/Services/IAiPromptTranslator.cs tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs
git commit -m "feat(ai): add Vietnamese prompt translation contract"
```

### Task 2: Implement tested Cloudflare M2M100 adapter

**Files:**
- Create: `src/RecipeCard.Web/Services/CloudflareWorkersAiPromptTranslator.cs`
- Modify: `tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs`

- [x] **Step 1: Write failing transport tests using an in-memory `HttpMessageHandler`.**

Use non-secret fixture values `cf-account-id` and `test-token`. Assert both supported successful response envelopes:

```json
{"result":{"translated_text":"Pour coconut milk into a glass with ice."}}
```

```json
{"result":[{"translated_text":"Pour coconut milk into a glass with ice."}]}
```

Each response must cause `TranslateVietnameseToEnglishAsync("Đong sữa dừa vào ly có đá.")` to return exactly that English text.

Assert the outbound request has:

```text
POST https://api.cloudflare.com/client/v4/accounts/cf-account-id/ai/run/@cf/meta/m2m100-1.2b
Authorization: Bearer test-token
Content-Type: application/json
body: { "text": "...", "source_lang": "vi", "target_lang": "en" }
```

Add failing tests for: non-2xx Cloudflare `errors[0].message`; missing/blank `result.translated_text` in either supported envelope; malformed JSON; transport failure. Each public exception must be Vietnamese, must not include token/account ID/raw response, and must never return the original Vietnamese prompt.

- [x] **Step 2: Run the focused test and confirm it fails.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiPromptTranslatorTests"
```

Expected: failures because the adapter does not yet exist.

- [x] **Step 3: Implement `CloudflareWorkersAiPromptTranslator`.**

Implement constructor injection matching existing `CloudflareWorkersAiImageGenerator`:

```csharp
public CloudflareWorkersAiPromptTranslator(
    HttpClient httpClient,
    IOptions<CloudflareOptions> options)
```

Behavior:

1. Reject null/whitespace `vietnamesePrompt` before an HTTP request.
2. Call `_options.Validate()`.
3. Resolve model from `TranslationModel`, defaulting only when it is whitespace.
4. POST JSON containing `text`, `source_lang = "vi"`, `target_lang = "en"` to the documented account/model endpoint.
5. Parse `result.translated_text` for the synchronous envelope and `result[0].translated_text` for the documented batch envelope. Trim it and reject blank output.
6. Map known non-2xx Cloudflare message to `Lỗi từ Cloudflare AI: {message}`; otherwise use a safe generic translation failure message.
7. Wrap network and invalid successful-payload failures in safe Vietnamese `InvalidOperationException` messages. Preserve `OperationCanceledException`.
8. Do not log or include the authorization token, account ID, request body, or raw provider response in exceptions.

- [x] **Step 4: Run focused translator tests.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiPromptTranslatorTests"
```

Expected: all translator tests pass, proving the exact request shape and safe failure behavior.

- [x] **Step 5: Commit the adapter.**

```bash
git add src/RecipeCard.Web/Services/CloudflareWorkersAiPromptTranslator.cs tests/RecipeCard.Web.Tests/AiPromptTranslatorTests.cs
git commit -m "feat(ai): translate Vietnamese prompts with Cloudflare"
```

### Task 3: Make prompt construction language-safe at the seam

**Files:**
- Modify: `src/RecipeCard.Web/Services/AiImagePromptBuilder.cs`
- Modify: `tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs`

- [x] **Step 1: Replace old canonical-prompt assertions with source/final prompt assertions.**

The test should establish two contracts:

```csharp
var source = builder.BuildVietnameseSourcePrompt(
    "Matcha sữa dừa", 2, "Đong sữa dừa vào ly có đá", "ly thủy tinh");
var final = builder.BuildFinalImagePrompt(
    "Beverage recipe: Coconut milk matcha. Step 2: Pour coconut milk into a glass with ice.");
```

Assert `source` retains Vietnamese recipe name, step instruction, bounded brief, and has no English photography guideline. Assert `final` retains the supplied English text and appends `DefaultStyleGuideline` exactly once.

- [x] **Step 2: Run the builder tests and confirm they fail.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiImageCandidateTests"
```

Expected: compilation or assertion failure because source/final methods do not yet exist.

- [x] **Step 3: Split `AiImagePromptBuilder` into explicit source and final methods.**

Expose:

```csharp
string BuildVietnameseSourcePrompt(string recipeName, int stepOrder, string instruction, string? userBrief);
string BuildFinalImagePrompt(string translatedEnglishPrompt);
```

`BuildVietnameseSourcePrompt` must:
- validate nonblank recipe name and instruction;
- preserve the existing 500-character `userBrief` cap;
- include only Vietnamese labels/context and the optional Vietnamese visual note;
- omit the fixed photography guideline.

`BuildFinalImagePrompt` must reject blank translated text and append the existing English `DefaultStyleGuideline` exactly once. Delete/replace the old `BuildPrompt` API and migrate every caller; do not retain an alias.

- [x] **Step 4: Run builder tests.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiImageCandidateTests"
```

Expected: source data stays Vietnamese before translation; final output consists of English translation plus the fixed style only once.

- [x] **Step 5: Commit the builder split.**

```bash
git add src/RecipeCard.Web/Services/AiImagePromptBuilder.cs tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs
git commit -m "refactor(ai): separate Vietnamese prompt source from image style"
```

### Task 4: Wire translation into create and regenerate flows

**Files:**
- Modify: `src/RecipeCard.Web/Program.cs`
- Modify: `src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs`
- Modify: `tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs`
- Modify: `src/RecipeCard.Web/appsettings.json`

- [x] **Step 1: Write failing editor integration tests.**

Add `FakeAiPromptTranslator : IAiPromptTranslator` with `TranslationToReturn`, `PromptsReceived`, and optional failure. Update `FakeAiGenerator` to capture its received prompt.

Prove both handlers:

1. `OnPostGenerateAiImageAsync` calls translator once with Vietnamese source text, calls generator with translated English + fixed style, and stores that final English prompt in `AiImageDraft.PromptSnapshot`.
2. `OnPostRegenerateAiImageAsync` follows the same sequence.
3. Translator exception causes no `AiImageDraft` record, no draft file, and no Cloudinary upload; its safe Vietnamese message appears in `ErrorMessage`.

- [x] **Step 2: Run the candidate tests and confirm the new cases fail.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiImageCandidateTests"
```

Expected: constructor/flow failures because translator is not injected or called yet.

- [x] **Step 3: Register the typed client and introduce one shared page-model helper.**

In `Program.cs`, add a typed client with an explicit 30-second timeout:

```csharp
builder.Services.AddHttpClient<IAiPromptTranslator, CloudflareWorkersAiPromptTranslator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

In `EditModel`:

1. Inject `IAiPromptTranslator?` alongside existing optional AI dependencies.
2. Fail early with `Dịch vụ dịch prompt AI chưa được cấu hình.` if it is unavailable.
3. Add one private async helper that calls `BuildVietnameseSourcePrompt`, invokes `TranslateVietnameseToEnglishAsync`, then calls `BuildFinalImagePrompt`.
4. Use that helper at both former `BuildPrompt` call sites: `OnPostGenerateAiImageAsync` and `OnPostRegenerateAiImageAsync`.
5. Catch translation failures separately and set `ErrorMessage` to `Dịch mô tả cho AI thất bại: {safe message}`. Return immediately; do not call FLUX and do not create a draft.
6. Leave candidate state checks, temporary file storage, acceptance, and Cloudinary behavior untouched.

In tracked `appsettings.json`, add only:

```json
"TranslationModel": "@cf/meta/m2m100-1.2b"
```

inside the existing `Cloudflare` section. Do not add Account ID, token, or Cloudinary secret.

- [x] **Step 4: Run candidate integration tests.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiImageCandidateTests"
```

Expected: creation/regeneration call translator then generator; translation failure creates no candidate; acceptance lifecycle remains green.

- [x] **Step 5: Commit flow wiring.**

```bash
git add src/RecipeCard.Web/Program.cs src/RecipeCard.Web/Pages/Recipes/Edit.cshtml.cs src/RecipeCard.Web/appsettings.json tests/RecipeCard.Web.Tests/AiImageCandidateTests.cs
git commit -m "feat(ai): translate SOP prompts before image generation"
```

### Task 5: Verify the feature without exposing credentials

**Files:**
- No source changes unless a verification defect is found.

- [x] **Step 1: Run the translation and candidate suites.**

Run:

```bash
dotnet test --filter "FullyQualifiedName~AiPromptTranslatorTests|FullyQualifiedName~AiImageCandidateTests"
```

Expected: all focused tests pass.

- [x] **Step 2: Run the complete automated test suite.**

Run:

```bash
dotnet test
```

Expected: all tests pass with no locked application process.

- [x] **Step 3: Manual authenticated-provider smoke check.**

With Cloudflare credentials held only in User Secrets or ignored `appsettings.Development.json`:

1. Start `RecipeCard.Web`.
2. Open an existing recipe step whose instruction is Vietnamese: `Đong sữa dừa vào ly có đá.`
3. Create an AI draft.
4. Confirm a candidate image is shown and the draft `PromptSnapshot` is English with the single fixed photography guideline.
5. Reject the draft; confirm it does not enter the media library or Cloudinary.

Do not paste credentials, complete raw provider payloads, or images containing private material into source control or chat.

- [x] **Step 4: Commit any verification-only correction, otherwise do not make a no-op commit.**

## Acceptance criteria

- Every current Vietnamese prompt is translated through `@cf/meta/m2m100-1.2b` before it reaches FLUX.
- The M2M100 call sends the documented `text`, `source_lang: "vi"`, `target_lang: "en"` payload and consumes only `result.translated_text`.
- FLUX receives only translated English context plus the fixed English photography guideline.
- Generate and regenerate use the same translation path.
- Failure to translate leaves existing recipe data and accepted assets unchanged, creates no draft, and never falls back to raw Vietnamese FLUX input.
- Candidate `PromptSnapshot` records the final English prompt actually sent to FLUX.
- Existing Cloudflare credentials stay server-side; no new secret or browser-facing configuration is introduced.
- No language detector or English-bypass branch is added in this phase.

## Deferred phase

After this plan is shipped and evaluated with real SOP prompts, add a separate plan for language detection: confidently English input skips M2M100; Vietnamese or uncertain/mixed input uses translation. That decision is intentionally excluded here.
