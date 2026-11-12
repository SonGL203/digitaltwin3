namespace Warehouse.Api.Services;

using Warehouse.Api.Data;
using Warehouse.Api.Models;
using Warehouse.Api.Rules;
using Warehouse.Api.ViewModels;

public interface IPalletService
{
    ServiceResult<IReadOnlyList<ProductTypeViewModel>> GetProductTypes();
    ServiceResult<ProductTypeViewModel> CreateProductType(ProductTypeRequest request);
    ServiceResult<BatchCheckViewModel> CheckBatch(BatchRequest request);
    ServiceResult<BatchReceivedViewModel> ReceiveBatch(BatchRequest request);
    ServiceResult<AllocationViewModel> Allocate(int roomId, int batchId);
    ServiceResult<DistributionViewModel> Distribute(int batchId);
    ServiceResult<IReadOnlyList<SlotViewModel>> GetSlots(int roomId);
    ServiceResult<ResetViewModel> ResetPlacements(int roomId);
}

/// <summary>
/// Nhập lô và xếp pallet. Chỉ điều phối: lấy dữ liệu từ store → hỏi Rules (PalletSizer, SlotFinder, Allocator) →
/// áp kết quả (Place) → lưu. Không tự viết lại luật chọn khung, nên số trong xem trước và khi xếp thật khớp nhau
/// (cùng SlotFinder/Allocator).
/// Lập lô trước, chọn kho sau. Ba bước tách riêng để bấm lại không xếp đôi: CheckBatch (kiểm tra mọi kho, không ghi gì)
/// → ReceiveBatch (lưu lô, pallet chưa vào khung) → Distribute (tự chia các kho) hoặc Allocate (một kho người dùng chọn);
/// cả hai chỉ xếp pallet của lô CHƯA nằm trong khung nào, gọi lần nữa thì không có gì để xếp.
/// </summary>
public class PalletService(IWarehouseStore store) : IPalletService
{
    /// <summary>Chặn lô quá lớn: Allocator duyệt (pallet × khung) hai lượt, nên giới hạn để một request không treo máy.</summary>
    public const int MaxPalletsPerBatch = 2000;

    private sealed record ResolvedLine(BatchLineRequest Line, PalletSize Size, bool IsNew);

    // ---------- loại hàng ----------

    public ServiceResult<IReadOnlyList<ProductTypeViewModel>> GetProductTypes()
        => ServiceResult<IReadOnlyList<ProductTypeViewModel>>.Ok(
            store.GetProductTypes().Select(t => new ProductTypeViewModel(t.Id, t.Code, t.Name)).ToList());

    public ServiceResult<ProductTypeViewModel> CreateProductType(ProductTypeRequest request)
    {
        ProductType type;
        try
        {
            type = new ProductType(request.Code, request.Name);
        }
        catch (ArgumentException ex)
        {
            return ServiceResult<ProductTypeViewModel>.Fail(ServiceError.Invalid, ex.Message);
        }

        if (store.GetProductTypes().Any(t => string.Equals(t.Code, type.Code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<ProductTypeViewModel>.Fail(ServiceError.Conflict, $"Đã có loại hàng mã {type.Code}.");

        store.AddProductType(type);
        return ServiceResult<ProductTypeViewModel>.Ok(new ProductTypeViewModel(type.Id, type.Code, type.Name));
    }

    // ---------- kiểm tra lô (không ghi gì) ----------

    /// <summary>
    /// Lập lô trước, chọn kho sau: khớp từng dòng với type, rồi với MỌI kho tính xem nếu nhận riêng cả lô thì xếp được
    /// bao nhiêu, thiếu bao nhiêu (kèm lý do theo type); cuối cùng là đề xuất chia kho (Distributor).
    /// Chạy đúng Allocator/Distributor như khi xếp thật (trên pallet tạm trong RAM) nên con số khớp với kết quả thật.
    /// </summary>
    public ServiceResult<BatchCheckViewModel> CheckBatch(BatchRequest request)
    {
        if (CheckRequest(request) is { } requestError)
            return ServiceResult<BatchCheckViewModel>.Fail(ServiceError.Invalid, requestError);

        var (resolved, error) = ResolveLines(request, save: null);
        if (resolved is null)
            return ServiceResult<BatchCheckViewModel>.Fail(ServiceError.Invalid, error!);

        // Pallet tạm (chưa lưu, Id = 0): đủ cho SlotFinder/Allocator vì chỉ đọc số đo qua Size.
        var palletsOfLine = resolved
            .Select(r => Enumerable.Range(0, r.Line.Quantity)
                .Select(_ => new Pallet(0, r.Size, "xem-truoc", r.Line.ProductTypeId)).ToList())
            .ToList();
        var lineOf = new Dictionary<Pallet, int>();
        for (var i = 0; i < palletsOfLine.Count; i++)
            foreach (var pallet in palletsOfLine[i])
                lineOf[pallet] = i;
        var all = palletsOfLine.SelectMany(p => p).ToList();

        var rooms = AllRooms();
        var roomChecks = rooms.Select(room =>
        {
            var allocation = Allocator.Allocate(all, room.Candidates, room.Margin);
            var placeable = allocation.Placements.GroupBy(p => lineOf[p.Pallet]).ToDictionary(g => g.Key, g => g.Count());
            var notes = new List<string>();
            for (var i = 0; i < resolved.Count; i++)
            {
                var search = SlotFinder.Find(room.Candidates, palletsOfLine[i][0], room.Margin);
                var note = Shortage(room.Candidates.Count, search, resolved[i].Size.Code, resolved[i].Line.Quantity, placeable.GetValueOrDefault(i));
                if (note is not null)
                    notes.Add(note);
            }
            return new RoomCheckViewModel(room.RoomId, room.Name, allocation.Placements.Count,
                all.Count - allocation.Placements.Count, notes.Distinct().ToList());
        }).ToList();

        var plan = Distributor.Distribute(all, rooms, preferredRoomId: null);

        var lines = resolved.Select(r => new BatchLineCheckViewModel(
            r.Line.Quantity, r.Size.Code, r.IsNew, r.Size.Color, r.Size.Length, r.Size.Width, r.Size.Height, r.Size.Weight,
            r.Line.ProductTypeId)).ToList();

        return ServiceResult<BatchCheckViewModel>.Ok(new BatchCheckViewModel(
            all.Count, lines, roomChecks, ToViewModel(null, all.Count, plan, withPlacements: false)));
    }

    /// <summary>Lý do một dòng không vào đủ một kho; null khi vào đủ.</summary>
    private static string? Shortage(int slotCount, SlotSearchResult search, string typeCode, int needed, int placeable)
    {
        if (!search.HasAnyFittingSlot)
        {
            return slotCount == 0
                ? "Kho chưa có khung nào."
                : $"Không có kệ thích hợp cho type {typeCode}: {string.Join(" ", search.RejectionReasons)}";
        }
        if (placeable == 0)
            return $"Các kệ thích hợp cho type {typeCode} đã đầy.";
        if (placeable < needed)
            return $"Type {typeCode}: xếp được {placeable} / {needed}, thiếu {needed - placeable} ô.";
        return null;
    }

    // ---------- nhập lô ----------

    /// <summary>
    /// Lưu type mới (nếu có), lô và các pallet (b1-P001…) trong MỘT transaction. Pallet chưa vào khung nào:
    /// việc xếp là Allocate. Kiểm tra hết trước (chạy thử việc khớp type) để lỗi không làm lưu dở.
    /// </summary>
    public ServiceResult<BatchReceivedViewModel> ReceiveBatch(BatchRequest request)
    {
        if (CheckRequest(request) is { } requestError)
            return ServiceResult<BatchReceivedViewModel>.Fail(ServiceError.Invalid, requestError);

        var (dryRun, error) = ResolveLines(request, save: null);
        if (dryRun is null)
            return ServiceResult<BatchReceivedViewModel>.Fail(ServiceError.Invalid, error!);

        Batch? batch = null;
        store.InTransaction(() =>
        {
            // Lưu type mới trước: Pallet nhớ SizeId lúc tạo nên type phải có Id rồi.
            var (resolved, _) = ResolveLines(request, save: store.AddPalletSize);

            batch = new Batch($"b{store.CountBatches() + 1}", DateTime.UtcNow);
            var number = 0;
            foreach (var (line, size, _) in resolved!)
            {
                for (var i = 0; i < line.Quantity; i++)
                    batch.Pallets.Add(new Pallet(0, size, $"{batch.Code}-P{++number:000}", line.ProductTypeId));
            }

            store.AddBatch(batch);   // lô và pallet được gán Id
        });

        return ServiceResult<BatchReceivedViewModel>.Ok(
            new BatchReceivedViewModel(batch!.Id, batch.Code, batch.Pallets.Count));
    }

    // ---------- xếp ----------

    /// <summary>
    /// Xếp các pallet CHƯA nằm trong khung nào của lô vào phòng. Trong một transaction: đọc khung, Allocator chọn chỗ,
    /// Place() từng pallet, lưu khung. Pallet không xếp được (không vừa, hết chỗ) vẫn thuộc lô, chưa có khung;
    /// thêm kệ rồi bấm Xếp lại sẽ xếp tiếp phần còn lại. Mọi pallet đã xếp thì trả danh sách rỗng, không lỗi.
    /// </summary>
    public ServiceResult<AllocationViewModel> Allocate(int roomId, int batchId)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<AllocationViewModel>(roomId);

        ServiceResult<AllocationViewModel> outcome = default!;
        store.InTransaction(() =>
        {
            var batch = store.GetBatch(batchId);
            if (batch is null)
            {
                outcome = ServiceResult<AllocationViewModel>.Fail(ServiceError.NotFound, $"Không có lô Id {batchId}.");
                return;
            }

            // "Đã xếp" xét trên MỌI phòng: pallet nằm trong khung của phòng khác thì không xếp thêm lần nữa.
            var placedIds = store.GetAllSlots().Where(s => s.PalletId is not null).Select(s => s.PalletId!.Value).ToHashSet();
            var unplaced = batch.Pallets.Where(p => !placedIds.Contains(p.Id)).OrderBy(p => p.Id).ToList();

            var allocation = Allocator.Allocate(unplaced, PlacementOrder(room), room.Margin);

            foreach (var placement in allocation.Placements)
                placement.Target.Slot.Place(placement.Pallet);
            store.UpdateSlots(allocation.Placements.Select(p => p.Target.Slot).ToList());

            outcome = ServiceResult<AllocationViewModel>.Ok(new AllocationViewModel(
                batch.Id,
                allocation.Placements.Select(ToViewModel).ToList(),
                allocation.Rejected.Select(r => new RejectionViewModel(r.Pallet.Code, r.Reason)).ToList(),
                batch.Pallets.Count - unplaced.Count));
        });

        return outcome;
    }

    // ---------- chia lô ra nhiều kho ----------

    /// <summary>
    /// Nhập theo đề xuất: chia các pallet CHƯA có khung của lô ra các kho (ưu tiên kho đã có type trùng), trong MỘT
    /// transaction. Cùng Distributor với CheckBatch nên khớp đề xuất. Bấm lại thì chỉ còn pallet chưa có khung: không xếp đôi.
    /// </summary>
    public ServiceResult<DistributionViewModel> Distribute(int batchId)
    {
        ServiceResult<DistributionViewModel> outcome = default!;
        store.InTransaction(() =>
        {
            var batch = store.GetBatch(batchId);
            if (batch is null)
            {
                outcome = ServiceResult<DistributionViewModel>.Fail(ServiceError.NotFound, $"Không có lô Id {batchId}.");
                return;
            }

            var placedIds = store.GetAllSlots().Where(s => s.PalletId is not null).Select(s => s.PalletId!.Value).ToHashSet();
            var unplaced = batch.Pallets.Where(p => !placedIds.Contains(p.Id)).OrderBy(p => p.Id).ToList();

            var result = Distributor.Distribute(unplaced, AllRooms(), preferredRoomId: null);
            var all = result.Shares.SelectMany(s => s.Placements).ToList();
            foreach (var placement in all)
                placement.Target.Slot.Place(placement.Pallet);
            store.UpdateSlots(all.Select(p => p.Target.Slot).ToList());

            outcome = ServiceResult<DistributionViewModel>.Ok(ToViewModel(batch.Id, unplaced.Count, result, withPlacements: true));
        });

        return outcome;
    }

    private List<RoomSlots> AllRooms()
        => store.GetRooms().Select(r => new RoomSlots(r.Id, r.Name, PlacementOrder(r), r.Margin)).ToList();

    private static DistributionViewModel ToViewModel(int? batchId, int total, DistributionResult result, bool withPlacements)
        => new(
            batchId,
            total,
            result.Shares.Sum(s => s.Placements.Count),
            result.Shares.Select(s => new RoomShareViewModel(
                s.RoomId, s.Name, s.Placements.Count,
                withPlacements ? s.Placements.Select(ToViewModel).ToList() : new List<PlacementViewModel>())).ToList(),
            result.Rejected.Select(r => new RejectionViewModel(r.Pallet.Code, r.Reason)).ToList());

    private static PlacementViewModel ToViewModel(Placement p)
        => new(p.Pallet.Code, p.Pallet.Size.Code, p.Pallet.Size.Color,
               p.Target.Rack.Code, p.Target.Slot.Code, p.Target.Slot.Level, p.Target.Slot.Bay, p.Target.Slot.IsTop);

    // ---------- đọc khung, đặt lại ----------

    /// <summary>Mọi khung của phòng (theo kệ → tầng → ô) kèm pallet/type/màu nếu đang có hàng. Dùng để vẽ 3D.</summary>
    public ServiceResult<IReadOnlyList<SlotViewModel>> GetSlots(int roomId)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<IReadOnlyList<SlotViewModel>>(roomId);

        var candidates = CandidatesOf(room);
        var palletIds = candidates.Select(c => c.Slot.PalletId).OfType<int>().ToList();
        var pallets = store.GetPallets(palletIds).ToDictionary(p => p.Id);

        var slots = candidates.Select(c =>
        {
            var pallet = c.Slot.PalletId is int id ? pallets.GetValueOrDefault(id) : null;
            return new SlotViewModel(
                c.Slot.Id, c.Rack.Id, c.Rack.Code, c.Slot.Code, c.Slot.Level, c.Slot.Bay, c.Slot.IsTop,
                c.Slot.PalletId, pallet?.Code, pallet?.Size.Code, pallet?.Size.Color);
        }).ToList();

        return ServiceResult<IReadOnlyList<SlotViewModel>>.Ok(slots);
    }

    /// <summary>
    /// "Đặt lại": lấy mọi pallet ra khỏi khung của phòng (pallet và lô vẫn còn, chỉ chưa có khung), trong một transaction.
    /// Sau đó xếp lại bằng Allocate.
    /// </summary>
    public ServiceResult<ResetViewModel> ResetPlacements(int roomId)
    {
        var room = store.GetRoom(roomId);
        if (room is null)
            return NoRoom<ResetViewModel>(roomId);

        var cleared = 0;
        store.InTransaction(() =>
        {
            var full = store.GetSlotsOfRacks(room.Racks.Select(r => r.Id).ToList()).Where(s => !s.IsEmpty).ToList();
            foreach (var slot in full)
                slot.Remove();
            store.UpdateSlots(full);
            cleared = full.Count;
        });

        return ServiceResult<ResetViewModel>.Ok(new ResetViewModel(cleared));
    }

    // ---------- dùng chung ----------

    private static string? CheckRequest(BatchRequest request)
    {
        if (request.Lines.Count == 0)
            return "Lô phải có ít nhất một dòng.";
        if (request.Lines.Sum(l => (long)l.Quantity) > MaxPalletsPerBatch)
            return $"Một lô tối đa {MaxPalletsPerBatch} pallet.";
        return null;
    }

    /// <summary>
    /// Khớp từng dòng với một type (có sẵn hoặc mới) bằng PalletSizer. Type mới của dòng trước được tính vào danh sách
    /// cho dòng sau (hai dòng cùng số đo dùng chung một type). save: ghi type mới xuống store (null = chạy thử, không ghi).
    /// </summary>
    private (List<ResolvedLine>? Lines, string? Error) ResolveLines(BatchRequest request, Action<PalletSize>? save)
    {
        var productTypeIds = store.GetProductTypes().Select(t => t.Id).ToHashSet();
        var sizes = store.GetPalletSizes().ToList();
        var resolved = new List<ResolvedLine>();

        foreach (var line in request.Lines)
        {
            if (line.ProductTypeId is { } typeId && !productTypeIds.Contains(typeId))
                return (null, $"Không có loại hàng Id {typeId}.");

            SizeResolution resolution;
            try
            {
                resolution = PalletSizer.Resolve(sizes, line.Length, line.Width, line.Height, line.Weight);
            }
            catch (ArgumentException ex)
            {
                return (null, ex.Message);
            }

            if (resolution.IsNew)
            {
                save?.Invoke(resolution.Size);
                sizes.Add(resolution.Size);
            }
            resolved.Add(new ResolvedLine(line, resolution.Size, resolution.IsNew));
        }

        return (resolved, null);
    }

    /// <summary>
    /// Thứ tự khung để XẾP: khung thường của MỌI kệ trước, khung nóc của mọi kệ sau cùng. Nóc là chỗ cuối cùng cho pallet
    /// quá cao (hoặc khi hết chỗ khác), nên không được chiếm trước một khung thường còn trống ở kệ khác.
    /// OrderBy ổn định: trong mỗi nhóm vẫn giữ thứ tự kệ → tầng → ô, nên kết quả vẫn tất định.
    /// </summary>
    private List<SlotCandidate> PlacementOrder(Room room)
        => CandidatesOf(room).OrderBy(c => c.Slot.IsTop).ToList();

    /// <summary>Mọi khung của phòng theo thứ tự kệ → tầng → ô (nóc cuối của từng kệ); dùng để đọc/hiển thị.</summary>
    private List<SlotCandidate> CandidatesOf(Room room)
    {
        var slotsOfRack = store.GetSlotsOfRacks(room.Racks.Select(r => r.Id).ToList()).ToLookup(s => s.RackId);
        return room.Racks
            .SelectMany(rack => slotsOfRack[rack.Id]
                .OrderBy(s => s.Level).ThenBy(s => s.Bay)
                .Select(slot => new SlotCandidate(rack, slot)))
            .ToList();
    }

    private static ServiceResult<T> NoRoom<T>(int roomId)
        => ServiceResult<T>.Fail(ServiceError.NotFound, $"Không có phòng Id {roomId}.");
}
