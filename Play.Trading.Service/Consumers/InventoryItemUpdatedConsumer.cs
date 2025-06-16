using System.Threading.Tasks;
using MassTransit;
using Play.Common;
using Play.Inventory.Contracts;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Consumer;

public class InventoryItemUpdatedConsumer : IConsumer<InventoryItemUpdated>
{
    private readonly IRepository<InventoryItem> _repository;

    public InventoryItemUpdatedConsumer(IRepository<InventoryItem> repository)
    {
        _repository = repository;
    }

    public async Task Consume(ConsumeContext<InventoryItemUpdated> context)
    {
        var message = context.Message;
        var inventoryItem = await _repository.GetAsync(item =>
            item.UserId == message.UserId && item.CatalogItemID == message.UserId);
        if (inventoryItem == null)
        {
            inventoryItem = new InventoryItem
            {
                CatalogItemID = message.UserId,
                UserId = message.UserId,
                Quantity = message.newTotalQuantity
            }; 
            await _repository.CreateAsync(inventoryItem);
        }
        else
        {
            inventoryItem.Quantity = message.newTotalQuantity;
            await _repository.UpdateAsync(inventoryItem);
        }

    }
}