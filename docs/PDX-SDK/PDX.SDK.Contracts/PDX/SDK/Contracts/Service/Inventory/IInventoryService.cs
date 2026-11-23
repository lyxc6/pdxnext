using System.Threading.Tasks;
using PDX.SDK.Contracts.Service.Inventory.Result;

namespace PDX.SDK.Contracts.Service.Inventory;

public interface IInventoryService
{
	Task<InventoryItemsResult> GetInventoryItems();
}
