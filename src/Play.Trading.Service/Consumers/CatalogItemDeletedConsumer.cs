using System.Threading.Tasks;
using MassTransit;
using Play.Catalog.Contracts;
using Play.Common;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Consumer;

public class CatalogItemDeletedConsumer : IConsumer<CatalogItemCreated>
{
    private readonly IRepository<CatalogItem> _catalogItemRepository;

    public CatalogItemDeletedConsumer(IRepository<CatalogItem> catalogItemRepository)
    {
        _catalogItemRepository = catalogItemRepository;
    }
    
    public async Task Consume(ConsumeContext<CatalogItemCreated> context)
    {
        var message = context.Message;
        // checks to see if the item with the ID already exists, 
        var item = await _catalogItemRepository.GetAsync(message.ItemId);

        // if it does not exist, we return 
        if (item is null)
        {
            return; 
        }
        //otherwise we update
        await _catalogItemRepository.DeleteAsync(item.Id); 
    }
}