using System.Threading.Tasks;
using MassTransit;
using Play.Catalog.Contracts;
using Play.Common;
using Play.Trading.Service.Entities;


namespace Play.Trading.Service.Consumer;

public class CatalogItemCreatedConsumer : IConsumer<CatalogItemCreated>
{
    private readonly IRepository<CatalogItem> _catalogItemRepository;

    public CatalogItemCreatedConsumer(IRepository<CatalogItem> catalogItemRepository)
    {
        _catalogItemRepository = catalogItemRepository;
    }
    
    public async Task Consume(ConsumeContext<CatalogItemCreated> context)
    {
        var message = context.Message;
        // checks to see if the item with the ID already exists, no duplicate entries 
        // even when message is received multiple times
        // data is created once
        var item = await _catalogItemRepository.GetAsync(message.ItemId);

        // if it does exist then we return 
        if (item is not null)
        {
            return;
        }
    
        // if it does not exist then we add it to our repository 
        item = new CatalogItem
        {
            Id = message.ItemId,
            Name = message.ItemName,
            Description = message.Description,
            Price = message.Price
        };

        await _catalogItemRepository.CreateAsync(item); 
    }
}