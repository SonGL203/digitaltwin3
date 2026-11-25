namespace Warehouse.Api.Services
{
    /// <summary>Chạy nền: lấy từng lần Lưu nháp của trang 2D ra khỏi hàng đợi và ghi xuống database (sinh tầng, khung).</summary>
    public class LayoutSaveWorker(LayoutSaveQueue queue, IServiceScopeFactory scopes, ILogger<LayoutSaveWorker> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                using var scope = scopes.CreateScope();
                var layoutService = scope.ServiceProvider.GetRequiredService<ILayoutService>();
                try
                {
                    layoutService.RunSave(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Transaction chưa commit thì database tự bỏ: kho giữ nguyên như trước khi bấm lưu
                    job.Finish("Failed", "Máy chủ đang tắt, chưa lưu. Hãy bấm Lưu nháp lại.");
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Kho {RoomId} lỗi khi lưu mặt bằng", job.RoomId);
                    job.Finish("Failed", "Lỗi khi lưu mặt bằng, hãy bấm Lưu nháp lại.");
                }
            }
        }
    }
}
