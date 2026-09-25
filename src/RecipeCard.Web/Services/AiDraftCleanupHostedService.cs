using Microsoft.EntityFrameworkCore;
using RecipeCard.Web.Data;
using RecipeCard.Web.Models;

namespace RecipeCard.Web.Services;

public class AiDraftCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiDraftCleanupHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<AiDraftCleanupHostedService> _logger = logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredDraftsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình dọn dẹp các bản nháp AI đã hết hạn.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupExpiredDraftsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecipeDbContext>();
        var draftStore = scope.ServiceProvider.GetRequiredService<IAiDraftFileStore>();

        var now = DateTime.UtcNow;
        var expiredDrafts = await db.AiImageDrafts
            .Where(d => d.State == AiDraftState.Generated && d.ExpiresUtc < now)
            .ToListAsync(cancellationToken);

        if (expiredDrafts.Count == 0)
        {
            return;
        }

        foreach (var draft in expiredDrafts)
        {
            draft.State = AiDraftState.Expired;
            draftStore.DeleteDraft(draft.TemporaryFileName);
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Đã dọn dẹp {Count} bản nháp AI hết hạn.", expiredDrafts.Count);
    }
}
