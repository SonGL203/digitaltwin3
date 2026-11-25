namespace Warehouse.Api.Services
{
    public class AllocationWorker(AllocationQueue queue, IServiceScopeFactory scopes, ILogger<AllocationWorker> logger)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<IBatchService>().ResumeUnfinished();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không đọc được các lô đang nhập / xếp dở");
            }

            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                using var scope = scopes.CreateScope();
                var batchService = scope.ServiceProvider.GetRequiredService<IBatchService>();
                try
                {
                    if (item.Import && item.Allocation != null)
                        batchService.RunImportAndAllocate(item.Allocation, stoppingToken);
                    else if (item.Import)
                        batchService.RunImport(item.BatchId, stoppingToken);
                    else
                        batchService.RunAllocation(item.Allocation!, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Lô {BatchId} lỗi khi {Work}", item.BatchId, item.Import ? "nhập" : "xếp");
                    if (item.Import)
                        queue.FinishImport(item.BatchId);
                    if (item.Allocation == null)
                        continue;
                    try
                    {
                        batchService.FailAllocation(item.Allocation, "Lỗi khi xếp lô, hãy bấm Xếp lại.");
                    }
                    catch (Exception saveEx)
                    {
                        logger.LogError(saveEx, "Không lưu được trạng thái lỗi của lô {BatchId}", item.BatchId);
                    }
                }
            }
        }
    }
}
