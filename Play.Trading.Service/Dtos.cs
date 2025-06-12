using System;
using System.ComponentModel.DataAnnotations;

namespace Play.Trading.Service;

public record SubmitPurchaseDto( 
    [Required]Guid? ItemId, 
    [Range(1,100)]int Quantity);

public record PurchaseDto(
    Guid UserId,
    Guid ItemId,
    decimal? PurchaseTotal,
    int Quantity,
    string State,
    string Reason, // error
    DateTimeOffset Received,
    DateTimeOffset LastUpdated
);



