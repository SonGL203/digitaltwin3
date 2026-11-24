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

        ServiceResult<AllocationViewModel> Allocate(int roomId, int batchId);
    }

    public class BatchService(IBatchStore store) : IBatchService
    {
        public const int MaxPalletsPerBatch = 1_000_000;

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
            if (store.GetRoomMargin(roomId) == null) return null;

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
                for (int i = 0; i < input.Quantity; i++)
                {
                    number++;
                    line.Pallets.Add(new Pallet { Code = $"{batch.Code}-P{number:000}" });
                }
                batch.Lines.Add(line);
            }

            store.SaveBatch(batch, newTypes);

            return new BatchCreatedViewModel(batch.Id, batch.Code, number);
        }

        public ServiceResult<AllocationViewModel> Allocate(int roomId, int batchId)
        {
            if (store.GetRoomMargin(roomId) == null)
                return ServiceResult<AllocationViewModel>.Fail(404, $"Không có phòng Id {roomId}.");
            if (!store.BatchExists(batchId))
                return ServiceResult<AllocationViewModel>.Fail(404, $"Không có lô Id {batchId}.");

            var pallets = store.GetUnplacedPallets(batchId);
            List<PalletGroup> palletGroups = Allocator.GroupPallets(pallets);

            var groups = new List<(PalletGroup Group, List<FreeSlot> FreeSlots)>();
            foreach (PalletGroup group in palletGroups)
            {
                List<FreeSlot> freeSlots = store.GetFreeSlots(roomId, group.Type, group.ProductTypeId);
                groups.Add((group, freeSlots));
            }

            var result = Allocator.Assign(groups);

            int updated = store.PlacePallets(
                result.Placements.Select(p => p.Slot.Id).ToArray(),
                result.Placements.Select(p => p.Pallet.Id).ToArray());
            if (updated < 0)
                return ServiceResult<AllocationViewModel>.Fail(409, "Có khung vừa bị lấp bởi thao tác khác, hãy bấm Xếp lại.");

            return ServiceResult<AllocationViewModel>.Ok(new AllocationViewModel(
                batchId,
                result.Placements.Select(p => new PlacementViewModel(
                    p.Pallet.Code, p.Pallet.Type.Code, p.Pallet.Type.Color,
                    p.Slot.RackCode, p.Slot.Code, p.Slot.IsTop)).ToList(),
                result.Rejected.Select(r => new RejectionViewModel(r.Pallet.Code, r.Reason)).ToList()));
        }
    }
}
