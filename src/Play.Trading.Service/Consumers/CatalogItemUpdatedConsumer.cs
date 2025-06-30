using System.Threading.Tasks;
using MassTransit;
using Play.Catalog.Contracts;
using Play.Common;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Consumer;

public class CatalogItemUpdatedConsumer : IConsumer<CatalogItemCreated>
{
    private readonly IRepository<CatalogItem> _catalogItemRepository;

    public CatalogItemUpdatedConsumer(IRepository<CatalogItem> catalogItemRepository)
    {
        _catalogItemRepository = catalogItemRepository;
    }
    
    public async Task Consume(ConsumeContext<CatalogItemCreated> context)
    {
        var message = context.Message;
        // checks to see if the item with the ID already exists, 
        var item = await _catalogItemRepository.GetAsync(message.ItemId);

        // if it does not exist, we create 
        if (item is null)
        {
            // if it does not exist then we add it to our repository 
            item = new CatalogItem
            {
                Id = message.ItemId,
                Name = message.ItemName,
                Description = message.Description,
                Price = message.Price
            };
        }
        //otherwise we update
        item.Name = message.ItemName;
        item.Description = message.Description;
        item.Price = message.Price;
        await _catalogItemRepository.UpdateAsync(item); 
    }
}