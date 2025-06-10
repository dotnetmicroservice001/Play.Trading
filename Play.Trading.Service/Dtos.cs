using System;
using System.ComponentModel.DataAnnotations;

namespace Play.Trading.Service;

public record SubmitPurchaseDto( 
    [Required]Guid? ItemId, 
    [Range(1,100)]int Quantity); 



