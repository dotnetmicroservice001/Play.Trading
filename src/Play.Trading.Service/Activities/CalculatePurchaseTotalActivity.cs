using System;
using System.Threading.Tasks;
using MassTransit;
using Play.Common;
using Play.Trading.Service.Entities;
using Play.Trading.Service.Exceptions;
using Play.Trading.Service.StateMachines;

namespace Play.Trading.Service.Activities;

// purchase state is the state of the state machine 
// purchase requested is the event that provides information to the activity 
public class CalculatePurchaseTotalActivity : IStateMachineActivity<PurchaseState, PurchaseRequested>
{
    private readonly IRepository<CatalogItem> _repository;

    public CalculatePurchaseTotalActivity(IRepository<CatalogItem> repository)
    {
        _repository = repository;
    }

    public void Probe(ProbeContext context)
    {
        context.CreateScope("Calculate-Purchase-TotalActivity");
    }

    public void Accept(StateMachineVisitor visitor)
    {
        visitor.Visit(this);
    }

    public async Task Execute(BehaviorContext<PurchaseState, 
        PurchaseRequested> context, IBehavior<PurchaseState, 
        PurchaseRequested> next)
    {
        var message = context.Message; 
        var item = await _repository.GetAsync(message.ItemId);
        if (item == null)
        {
            throw new UnknownItemException(message.ItemId);
        }
        
        context.Saga.PurchaseTotal = item.Price * message.Quantity;
        context.Saga.LastUpdated = DateTime.UtcNow;
        
        await next.Execute(context).ConfigureAwait(false);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<PurchaseState, 
        PurchaseRequested, TException> context, 
        IBehavior<PurchaseState, PurchaseRequested> next) where TException : Exception
    {
        return next.Faulted(context);
    }
}