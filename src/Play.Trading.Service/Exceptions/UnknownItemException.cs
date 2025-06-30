using System;

namespace Play.Trading.Service.Exceptions;

public class UnknownItemException : Exception
{
   
    public UnknownItemException(Guid itemId) : base($"Unknown item {itemId}") => this.ItemId = itemId;

    public Guid ItemId { get; }
}