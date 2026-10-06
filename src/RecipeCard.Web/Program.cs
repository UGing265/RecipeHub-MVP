using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = builder.Configuration["QuestPdf:LicenseType"] == "Enterprise"
    ? QuestPDF.Infrastructure.LicenseType.Enterprise
    : QuestPDF.Infrastructure.LicenseType.Community;

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.Configure<RecipeCard.Web.Services.CloudinaryOptions>(
    builder.Configuration.GetSection(RecipeCard.Web.Services.CloudinaryOptions.SectionName));
builder.Services.Configure<RecipeCard.Web.Services.CloudflareOptions>(
    builder.Configuration.GetSection(RecipeCard.Web.Services.CloudflareOptions.SectionName));
builder.Services.Configure<RecipeCard.Web.Services.GeminiOptions>(
    builder.Configuration.GetSection(RecipeCard.Web.Services.GeminiOptions.SectionName));

builder.Services.AddSingleton<RecipeCard.Web.Services.GeminiKeyCursor>();

builder.Services.AddSingleton<RecipeCard.Web.Services.IImageValidator, RecipeCard.Web.Services.ImageValidator>();
builder.Services.AddScoped<RecipeCard.Web.Services.CloudinaryImageStorageService>();
builder.Services.AddScoped<RecipeCard.Web.Services.ImageStorageService>();
builder.Services.AddScoped<RecipeCard.Web.Services.IImageStorageService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cloudName = config["Cloudinary:CloudName"];
    if (!string.IsNullOrWhiteSpace(cloudName))
    {
        return sp.GetRequiredService<RecipeCard.Web.Services.CloudinaryImageStorageService>();
    }
    return sp.GetRequiredService<RecipeCard.Web.Services.ImageStorageService>();
});

builder.Services.AddHttpClient<RecipeCard.Web.Services.IAiImageGenerator, RecipeCard.Web.Services.CloudflareWorkersAiImageGenerator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddHttpClient<RecipeCard.Web.Services.CloudflareWorkersAiPromptTranslator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<RecipeCard.Web.Services.IAiPromptTranslator, RecipeCard.Web.Services.GeminiRoundRobinPromptTranslator>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("MediaDelivery", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton<RecipeCard.Web.Services.IAiDraftFileStore, RecipeCard.Web.Services.AiDraftFileStore>();
builder.Services.AddSingleton<RecipeCard.Web.Services.IAiImagePromptBuilder, RecipeCard.Web.Services.AiImagePromptBuilder>();
builder.Services.AddScoped<RecipeCard.Web.Services.LegacyStepImageMigrationService>();
builder.Services.AddHostedService<RecipeCard.Web.Services.AiDraftCleanupHostedService>();
builder.Services.AddDbContext<RecipeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("RecipeDb")));

var app = builder.Build();

if (args.Contains("--migrate-step-images"))
{
    if (!args.Contains("--confirm"))
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("CẢNH BÁO: Cần cờ --confirm để tiến hành di chuyển ảnh bước thực hiện sang MediaAsset.");
        Console.WriteLine("Cú pháp: dotnet run -- --migrate-step-images --confirm");
        Console.ResetColor();
        return;
    }

    using var migrationScope = app.Services.CreateScope();
    var migrationService = migrationScope.ServiceProvider.GetRequiredService<RecipeCard.Web.Services.LegacyStepImageMigrationService>();
    var report = await migrationService.MigrateAsync();

    Console.WriteLine("=== BÁO CÁO DI CHUYỂN ẢNH CÔNG THỨC ===");
    Console.WriteLine($"Tổng số bước quét: {report.TotalScanned}");
    Console.WriteLine($"Thành công: {report.Converted}");
    Console.WriteLine($"Thiếu file trên đĩa: {report.Missing}");
    Console.WriteLine($"File không hợp lệ: {report.Invalid}");
    Console.WriteLine($"Lỗi tải lên nhà cung cấp: {report.ProviderFailed}");
    Console.WriteLine($"Lỗi lưu CSDL: {report.DatabaseFailed}");
    foreach (var detail in report.Details)
    {
        Console.WriteLine($" - {detail}");
    }
    return;
}

// Apply migrations and seed sample data automatically on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
    db.Database.Migrate();

    if (!db.Ingredients.Any())
    {
        var botSua = new RecipeCard.Web.Models.Ingredient { Name = "Bột sữa Phê La", NormalizedName = RecipeCard.Web.Models.Ingredient.Normalize("Bột sữa Phê La"), DefaultUnit = "g" };
        var hhsd = new RecipeCard.Web.Models.Ingredient { Name = "Hỗn hợp sữa đặc (HHSD)", NormalizedName = RecipeCard.Web.Models.Ingredient.Normalize("Hỗn hợp sữa đặc (HHSD)"), DefaultUnit = "g" };
        var duongNuoc = new RecipeCard.Web.Models.Ingredient { Name = "Đường nước", NormalizedName = RecipeCard.Web.Models.Ingredient.Normalize("Đường nước"), DefaultUnit = "g" };
        var cotTra = new RecipeCard.Web.Models.Ingredient { Name = "Trà Ô Long", NormalizedName = RecipeCard.Web.Models.Ingredient.Normalize("Trà Ô Long"), DefaultUnit = "ml" };
        var daVien = new RecipeCard.Web.Models.Ingredient { Name = "Đá viên", NormalizedName = RecipeCard.Web.Models.Ingredient.Normalize("Đá viên"), DefaultUnit = "g" };

        db.Ingredients.AddRange(botSua, hhsd, duongNuoc, cotTra, daVien);
        db.SaveChanges();

        var recipe = new RecipeCard.Web.Models.Recipe
        {
            Name = "Ô Long Sữa Phê La Lạnh",
            GeneralNote = "Thiết bị: Syphon. Lắc can trà ít nhất 10 nhịp trước khi sử dụng. Tăng/giảm trà: ± 10ml. Phục vụ kèm ống hút to.",
            Ingredients =
            [
                new RecipeCard.Web.Models.RecipeIngredient { Ingredient = botSua, Quantity = 22.5m },
                new RecipeCard.Web.Models.RecipeIngredient { Ingredient = hhsd, Quantity = 55m },
                new RecipeCard.Web.Models.RecipeIngredient { Ingredient = duongNuoc, Quantity = 6m },
                new RecipeCard.Web.Models.RecipeIngredient { Ingredient = cotTra, Quantity = 105m }
            ],
            Steps =
            [
                new RecipeStep { SortOrder = 1, Instruction = "Cho đá đầy miệng ly giấy (lắc nhẹ ly khi cho đá để lấp đầy khoảng trống)." },
                new RecipeStep { SortOrder = 2, Instruction = "Cho theo thứ tự các NVL: bột sữa, HHSD, đường nước vào ly giấy. Riêng cốt trà đong vào bình shaker." },
                new RecipeStep { SortOrder = 3, Instruction = "Thêm 1/2 xúc đá vào bình shaker rồi shake đều 15-20 nhịp." },
                new RecipeStep { SortOrder = 4, Instruction = "Đổ trà vào ly giấy đã setup trước đó, khuấy đều từ dưới lên kiểm tra dị vật và phục vụ." }
            ]
        };

        db.Recipes.Add(recipe);
        db.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
