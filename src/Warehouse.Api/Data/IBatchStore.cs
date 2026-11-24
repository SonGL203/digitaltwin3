using Warehouse.Api.Models;
using Warehouse.Api.Rules;

namespace Warehouse.Api.Data
{
    public interface IBatchStore
    {
        List<PalletType> GetPalletTypes();

        HashSet<int> GetProductTypeIds();

        double? GetRoomMargin(int roomId);

        int CountFreeSlots(int roomId, double length, double width, double height, double? weight, int? productTypeId);

        int NextBatchNumber();

        void SaveBatch(Batch batch, List<PalletType> newTypes);

        bool BatchExists(int batchId);

        List<Pallet> GetUnplacedPallets(int batchId);

        List<FreeSlot> GetFreeSlots(int roomId, PalletType type, int? productTypeId);

        int PlacePallets(int[] slotIds, int[] palletIds);
    }
}
