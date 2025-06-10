using System;

namespace Play.Trading.Service;

public record PurchaseRequested(Guid UserId, Guid ItemId, int Quantity, Guid CorrelationId); 

