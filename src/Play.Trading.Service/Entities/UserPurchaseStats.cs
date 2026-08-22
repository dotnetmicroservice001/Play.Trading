using System;
using Play.Common;

namespace Play.Trading.Service.Entities;

public class UserPurchaseStats : IEntity
{
    // Id == UserId, for a direct GetAsync(userId) lookup
    public Guid Id { get; set; }

    // Welford's online algorithm state for PurchaseTotal
    public long SampleCount { get; set; }
    public double Mean { get; set; }
    public double M2 { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}
