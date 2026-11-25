using Warehouse.Api.Models;
using Warehouse.Api.Rules;

namespace Warehouse.Api.Data
{
    public interface IBatchStore
    {
        List<PalletType> GetPalletTypes();

        HashSet<int> GetProductTypeIds();

        bool GetExistRoom(int roomId);

        int CountFreeSlots(int roomId, double length, double width, double height, double? weight, int? productTypeId);

        int NextBatchNumber();

        void SaveBatch(Batch batch, List<PalletType> newTypes);

        bool BatchExists(int batchId);

        void InsertPallets(string batchCode, int lineId, int from, int to);

        string? GetBatchCode(int batchId);

        List<LineImport> GetImportProgress(int batchId);

        List<int> GetUnfinishedImports();

        int CountUnplacedPallets(int batchId, int afterId);

        List<Pallet> GetUnplacedPallets(int batchId, int afterId, int limit);

        List<FreeSlot> GetFreeSlots(int roomId, PalletType type, int? productTypeId, int limit);

        int PlacePallets(int[] slotIds, int[] palletIds, AllocationProgress progress);

        AllocationProgress? GetAllocation(int batchId);

        List<AllocationProgress> GetUnfinishedAllocations();

        void SaveAllocation(AllocationProgress progress);
    }
}
