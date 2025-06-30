using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Play.Common;
using Play.Trading.Service.Entities;

namespace Play.Trading.Service.Controllers;
[ApiController]
[Route("store")]
[Authorize]
public class StoreController : ControllerBase
{
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<ApplicationUser> _userRepository;
    private readonly IRepository<InventoryItem> _inventoryRepository;

    public StoreController(
        IRepository<CatalogItem> catalogRepository, 
        IRepository<ApplicationUser> userRepository, 
        IRepository<InventoryItem> inventoryRepository)
    {
        _catalogRepository = catalogRepository;
        _userRepository = userRepository;
        _inventoryRepository = inventoryRepository;
    }

    [HttpGet]
    public async Task<ActionResult<StoreDto>> GetAsync()
    {
        //Find the value of the first claim where the claim type is 'sub'.
        string userId = User.FindFirstValue("sub");
        // all the catalog items
        var catalogItems = await _catalogRepository.GetAllAsync();
        // whatever the user has 
        var inventoryItems = await _inventoryRepository.GetAllAsync(item => item.UserId == Guid.Parse(userId)); 
        // the user 
        var user = await _userRepository.GetAsync(Guid.Parse(userId));

        var storeDto = new StoreDto(catalogItems.Select(
            catalogItem => new StoreItemDto(
                // list the fields of all catalog items 
                catalogItem.Id,
                catalogItem.Name,
                catalogItem.Description,
                catalogItem.Price,
                // query inventory find the first, if not found return zero 
                OwnedQuantity: inventoryItems.FirstOrDefault(inventoryItem => inventoryItem.CatalogItemID == catalogItem.Id)?
                    .Quantity ?? 0
                )
            ),
            // if user not found return 0 
            user?.Gil ?? 0 
        );
        return Ok(storeDto);
    }
}