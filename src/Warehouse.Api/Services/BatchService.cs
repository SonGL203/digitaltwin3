using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;
using Warehouse.Api.ViewModels;

namespace Warehouse.Api.Services
{
    public interface IBatchService
    {
        string? CheckBatch(CreateBatchRequest request);

        BatchCheckViewModel? CheckInRoom(int roomId, CreateBatchRequest request);

        string? CheckImportSize(CreateBatchRequest request);

        BatchCreatedViewModel CreateBatch(CreateBatchRequest request);

        ServiceResult<AllocationJobViewModel> StartAllocate(int roomId, int batchId);

        ServiceResult<AllocationJobViewModel> GetAllocation(int roomId, int batchId);

        void RunAllocation(AllocationJob job, CancellationToken stoppingToken);

        void ResumeUnfinished();

        void RunImport(int batchId, CancellationToken stoppingToken);

        void RunImportAndAllocate(AllocationJob job, CancellationToken stoppingToken);

        ServiceResult<BatchCreatedViewModel> CreateAndAllocate(int roomId, CreateBatchRequest request);

        ServiceResult<ImportStatusViewModel> GetImportStatus(int batchId);

        void FailAllocation(AllocationJob job, string error);
    }

    public class BatchService(IBatchStore store, AllocationQueue queue) : IBatchService
    {
        public const int MaxPalletsPerBatch = 1_000_000;
        public const int RoundSize = 50_000;
        public const int ImportChunk = 50_000;
        public const int MaxConflicts = 3;

        public string? CheckBatch(CreateBatchRequest request)
        {
            if (request.Lines == null || request.Lines.Count == 0) return "Lô phải có ít nhất một dòng.";
            if (request.Lines.Count > 50) return "Một lô tối đa 50 dòng.";

            var productTypeIds = store.GetProductTypeIds();
            for (int i = 0; i < request.Lines.Count; i++)
            {
                var line = request.Lines[i];
                var row = $"Dòng {i + 1}: ";
                if (line.Quantity < 1) return row + "số lượng phải ít nhất 1 pallet.";
                if (line.Length <= 0 || line.Length > 10) return row + "chiều dài phải lớn hơn 0 và tối đa 10 m.";
                if (line.Width <= 0 || line.Width > 10) return row + "chiều rộng phải lớn hơn 0 và tối đa 10 m.";
                if (line.Height <= 0 || line.Height > 10) return row + "chiều cao phải lớn hơn 0 và tối đa 10 m.";
                if (line.Weight != null && line.Weight <= 0) return row + "khối lượng phải lớn hơn 0 (hoặc để trống).";
                if (line.ProductTypeId != null && !productTypeIds.Contains(line.ProductTypeId.Value))
                    return row + $"không có loại hàng Id {line.ProductTypeId}.";
            }
            return null;
        }

        public BatchCheckViewModel? CheckInRoom(int roomId, CreateBatchRequest request)
        {
            if (!store.GetExistRoom(roomId)) return null;

            var types = store.GetPalletTypes();
            var lines = new List<BatchLineCheckViewModel>();
            var groups = new List<(PalletType Type, int? ProductTypeId, BatchLineRequest Size, long Need)>();

            foreach (var line in request.Lines)
            {
                var resolution = PalletSizer.Resolve(types, line.Length, line.Width, line.Height, line.Weight);
                if (resolution.IsNew)
                    types.Add(resolution.Type);

                lines.Add(new BatchLineCheckViewModel(
                    resolution.Type.Code, resolution.Type.Color, resolution.IsNew, line.Quantity,
                    line.Length, line.Width, line.Height));

                int index = groups.FindIndex(g => g.Type == resolution.Type && g.ProductTypeId == line.ProductTypeId);
                if (index < 0)
                    groups.Add((resolution.Type, line.ProductTypeId, line, line.Quantity));
                else
                    groups[index] = groups[index] with { Need = groups[index].Need + line.Quantity };
            }

            var notes = new List<string>();
            long placeable = 0;
            foreach (var group in groups)
            {
                var size = group.Size;
                int free = store.CountFreeSlots(roomId, size.Length, size.Width, size.Height, size.Weight, group.ProductTypeId);

                placeable += Math.Min(group.Need, free);
                if (free < group.Need)
                    notes.Add($"Type {group.Type.Code}: còn {free} khung trống vừa, cần {group.Need}.");
            }

            long total = request.Lines.Sum(l => (long)l.Quantity);
            return new BatchCheckViewModel(total, lines, placeable, total - placeable, notes);
        }

        public string? CheckImportSize(CreateBatchRequest request)
        {
            long total = request.Lines.Sum(l => (long)l.Quantity);
            if (total > MaxPalletsPerBatch)
                return $"Một lô nhập kho tối đa {MaxPalletsPerBatch} pallet (lô này có {total}).";
            return null;
        }

        public BatchCreatedViewModel CreateBatch(CreateBatchRequest request)
        {
            var created = SaveNewBatch(request);
            queue.PushImport(created.BatchId);
            return created;
        }

        public ServiceResult<BatchCreatedViewModel> CreateAndAllocate(int roomId, CreateBatchRequest request)
        {
            if (!store.GetExistRoom(roomId))
                return ServiceResult<BatchCreatedViewModel>.Fail(404, $"Không có phòng Id {roomId}.");

            var created = SaveNewBatch(request);
            var job = new AllocationJob(new AllocationProgress(created.BatchId, roomId, "Queued", 0, created.PalletCount, 0, 0, null));
            queue.TryAdd(job);
            store.SaveAllocation(job.Progress);
            queue.PushImportAndAllocate(job);
            return ServiceResult<BatchCreatedViewModel>.Ok(created);
        }

        private BatchCreatedViewModel SaveNewBatch(CreateBatchRequest request)
        {
            var types = store.GetPalletTypes();
            var newTypes = new List<PalletType>();
            var batch = new Batch { Code = $"b{store.NextBatchNumber()}" };
            int number = 0;

            foreach (var input in request.Lines)
            {
                var resolution = PalletSizer.Resolve(types, input.Length, input.Width, input.Height, input.Weight);
                if (resolution.IsNew)
                {
                    types.Add(resolution.Type);
                    newTypes.Add(resolution.Type);
                }

                var line = new BatchLine
                {
                    Quantity = input.Quantity,
                    Type = resolution.Type,
                    ProductTypeId = input.ProductTypeId,
                };
                number += input.Quantity;
                batch.Lines.Add(line);
            }

            store.SaveBatch(batch, newTypes);

            return new BatchCreatedViewModel(batch.Id, batch.Code, number);
        }

        public ServiceResult<AllocationJobViewModel> StartAllocate(int roomId, int batchId)
        {
            if (!store.GetExistRoom(roomId))
                return ServiceResult<AllocationJobViewModel>.Fail(404, $"Không có phòng Id {roomId}.");
            if (!store.BatchExists(batchId))
                return ServiceResult<AllocationJobViewModel>.Fail(404, $"Không có lô Id {batchId}.");

            var job = new AllocationJob(new AllocationProgress(batchId, roomId, "Queued", 0, 0, 0, 0, null));
            if (!queue.TryAdd(job))
            {
                var running = queue.FindRunning(batchId);
                if (running == null)
                    return ServiceResult<AllocationJobViewModel>.Fail(409, "Lô vừa xếp xong, hãy bấm Xếp lại.");
                if (running.RoomId != roomId)
                    return ServiceResult<AllocationJobViewModel>.Fail(409, $"Lô Id {batchId} đang được xếp vào phòng {running.RoomId}.");
                return ServiceResult<AllocationJobViewModel>.Ok(running.Snapshot());
            }

            store.SaveAllocation(job.Progress);
            queue.Push(job);
            return ServiceResult<AllocationJobViewModel>.Ok(job.Snapshot());
        }

        public ServiceResult<AllocationJobViewModel> GetAllocation(int roomId, int batchId)
        {
            var job = queue.Find(batchId);
            if (job != null)
            {
                if (job.RoomId != roomId)
                    return ServiceResult<AllocationJobViewModel>.Fail(404, $"Lô Id {batchId} chưa được xếp vào phòng {roomId}.");
                return ServiceResult<AllocationJobViewModel>.Ok(job.Snapshot());
            }

            var saved = store.GetAllocation(batchId);
            if (saved == null || saved.RoomId != roomId)
                return ServiceResult<AllocationJobViewModel>.Fail(404, $"Lô Id {batchId} chưa được xếp vào phòng {roomId}.");

            return ServiceResult<AllocationJobViewModel>.Ok(
                AllocationJob.ToViewModel(saved, new List<PlacementViewModel>(), new List<RejectionViewModel>()));
        }

        public void RunImport(int batchId, CancellationToken stoppingToken)
        {
            string? code = store.GetBatchCode(batchId);
            if (code != null)
            {
                foreach (LineImport line in store.GetImportProgress(batchId))
                {
                    int next = line.FirstNumber + line.Existing;
                    int last = line.FirstNumber + line.Quantity - 1;
                    while (next <= last)
                    {
                        if (stoppingToken.IsCancellationRequested) return;
                        int to = Math.Min(last, next + ImportChunk - 1);
                        store.InsertPallets(code, line.LineId, next, to);
                        next = to + 1;
                    }
                }
            }
            queue.FinishImport(batchId);
        }

        public ServiceResult<ImportStatusViewModel> GetImportStatus(int batchId)
        {
            string? code = store.GetBatchCode(batchId);
            if (code == null)
                return ServiceResult<ImportStatusViewModel>.Fail(404, $"Không có lô Id {batchId}.");

            int total = 0;
            int imported = 0;
            foreach (LineImport line in store.GetImportProgress(batchId))
            {
                total += line.Quantity;
                imported += line.Existing;
            }
            return ServiceResult<ImportStatusViewModel>.Ok(
                new ImportStatusViewModel(batchId, code, total, imported, imported >= total));
        }

        public void ResumeUnfinished()
        {
            foreach (int batchId in store.GetUnfinishedImports())
                queue.PushImport(batchId);

            foreach (AllocationProgress saved in store.GetUnfinishedAllocations())
            {
                var job = new AllocationJob(saved);
                if (queue.TryAdd(job))
                    queue.Push(job);
            }
        }

        public void FailAllocation(AllocationJob job, string error)
        {
            var failed = job.Progress with { Status = "Failed", Error = error };
            job.Update(failed);
            store.SaveAllocation(failed);
        }

        public void RunAllocation(AllocationJob job, CancellationToken stoppingToken)
        {
            AllocationProgress progress = StartJob(job, store.CountUnplacedPallets(job.BatchId, 0));
            var (state, after) = AllocateRounds(job, progress, stoppingToken);
            if (state == RoundState.Stopped) return;
            FinishJob(job, after);
        }

        public void RunImportAndAllocate(AllocationJob job, CancellationToken stoppingToken)
        {
            string? code = store.GetBatchCode(job.BatchId);
            List<LineImport> lines = store.GetImportProgress(job.BatchId);
            int total = 0;
            foreach (LineImport line in lines)
                total += line.Quantity;

            AllocationProgress progress = StartJob(job, total);
            bool full = false;
            if (code != null)
            {
                foreach (LineImport line in lines)
                {
                    int next = line.FirstNumber + line.Existing;
                    int last = line.FirstNumber + line.Quantity - 1;
                    while (next <= last)
                    {
                        if (stoppingToken.IsCancellationRequested) return;
                        int to = Math.Min(last, next + ImportChunk - 1);
                        store.InsertPallets(code, line.LineId, next, to);
                        next = to + 1;

                        if (full) continue;
                        var (state, after) = AllocateRounds(job, progress, stoppingToken);
                        if (state == RoundState.Stopped) return;
                        progress = after;
                        full = state == RoundState.Full;
                    }
                }
            }
            queue.FinishImport(job.BatchId);

            if (!full)
            {
                var (state, after) = AllocateRounds(job, progress, stoppingToken);
                if (state == RoundState.Stopped) return;
                progress = after;
            }
            FinishJob(job, progress);
        }

        private enum RoundState { Empty, Full, Stopped }

        private AllocationProgress StartJob(AllocationJob job, int total)
        {
            AllocationProgress progress = job.Progress;
            if (progress.Status != "Queued") return progress;

            progress = progress with
            {
                Status = "Running",
                AfterId = 0,
                Total = total,
                Placed = 0,
                RejectedCount = 0,
                Error = null,
            };
            store.SaveAllocation(progress);
            job.Update(progress);
            return progress;
        }

        private void FinishJob(AllocationJob job, AllocationProgress progress)
        {
            var done = progress with
            {
                Status = "Done",
                RejectedCount = progress.RejectedCount + store.CountUnplacedPallets(job.BatchId, progress.AfterId),
            };
            store.SaveAllocation(done);
            job.Update(done);
        }

        private (RoundState State, AllocationProgress Progress) AllocateRounds(
            AllocationJob job, AllocationProgress progress, CancellationToken stoppingToken)
        {
            int conflicts = 0;
            while (true)
            {
                if (stoppingToken.IsCancellationRequested) return (RoundState.Stopped, progress);

                List<Pallet> pallets = store.GetUnplacedPallets(job.BatchId, progress.AfterId, RoundSize);
                if (pallets.Count == 0) return (RoundState.Empty, progress);

                List<PalletGroup> palletGroups = Allocator.GroupPallets(pallets);
                var groups = new List<(PalletGroup Group, List<FreeSlot> FreeSlots)>();
                bool anyFreeSlot = false;
                foreach (PalletGroup group in palletGroups)
                {
                    List<FreeSlot> freeSlots = store.GetFreeSlots(job.RoomId, group.Type, group.ProductTypeId, RoundSize);
                    if (freeSlots.Count > 0) anyFreeSlot = true;
                    groups.Add((group, freeSlots));
                }

                AllocationResult result = Allocator.Assign(groups);

                int[] slotIds = new int[result.Placements.Count];
                int[] palletIds = new int[result.Placements.Count];
                var placed = new List<PlacementViewModel>();
                for (int i = 0; i < result.Placements.Count; i++)
                {
                    Placement p = result.Placements[i];
                    slotIds[i] = p.Slot.Id;
                    palletIds[i] = p.Pallet.Id;
                    placed.Add(new PlacementViewModel(
                        p.Pallet.Code, p.Pallet.Type.Code, p.Pallet.Type.Color, p.Slot.RackCode, p.Slot.Code, p.Slot.IsTop));
                }

                var rejected = new List<RejectionViewModel>();
                foreach (Rejection r in result.Rejected)
                    rejected.Add(new RejectionViewModel(r.Pallet.Code, r.Reason));

                var next = progress with
                {
                    AfterId = pallets[pallets.Count - 1].Id,
                    Placed = progress.Placed + placed.Count,
                    RejectedCount = progress.RejectedCount + rejected.Count,
                };

                if (store.PlacePallets(slotIds, palletIds, next) < 0)
                {
                    conflicts++;
                    if (conflicts >= MaxConflicts)
                    {
                        FailAllocation(job, "Có khung vừa bị lấp bởi thao tác khác, hãy bấm Xếp lại.");
                        return (RoundState.Stopped, progress);
                    }
                    continue;
                }
                conflicts = 0;

                job.AddRound(next, placed, rejected);
                progress = next;
                if (!anyFreeSlot) return (RoundState.Full, progress);
            }
        }
    }
}
