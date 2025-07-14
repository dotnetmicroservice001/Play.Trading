using System;
using Automatonymous;
using MassTransit;
using Microsoft.Extensions.Logging;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service.Activities;
using Play.Trading.Service.SignalR;

namespace Play.Trading.Service.StateMachines;

public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
{
    private readonly MessageHub _messageHub;
    private readonly ILogger<PurchaseStateMachine> _logger;
    public State Accepted { get; }
    
    public State ItemsGranted { get; }
    
    public State Completed { get; }

    public State Faulted { get; }

    // declare an event
    public Event<PurchaseRequested> PurchaseRequested { get;  }
    public Event<GetPurchaseState> GetPurchaseState { get;  }
    public Event<InventoryItemsGranted> InventoryItemsGranted { get; }
    public Event<GilDebited> GilDebited { get; }
    public Event<Fault<GrantItems>> GrantItemsFaulted { get; }
    public Event<Fault<DebitGil>> DebitGilFaulted { get; }
    
    
    public PurchaseStateMachine(MessageHub messageHub, ILogger<PurchaseStateMachine> logger)
    {
        _messageHub = messageHub;
        _logger = logger;
        InstanceState(state => state.CurrentState);
        ConfigureEvents();
        ConfigureInitialState();
        ConfigureAny();
        ConfigureAccepted();
        ConfigureItemsGranted();
        ConfigureFaulted();
        ConfigureCompleted();
    }
    
    private void ConfigureEvents()
    {
        Event(() => PurchaseRequested); 
        Event(() => GetPurchaseState);
        Event(() => InventoryItemsGranted);
        Event(() => GilDebited);
        Event(() => GrantItemsFaulted, x => x.CorrelateById(
            context => context.Message.Message.CorrelationId));
        Event(() => DebitGilFaulted, x => x.CorrelateById(
            context => context.Message.Message.CorrelationId));
        
    }
    private void ConfigureInitialState()
    {
        Initially(
            When(PurchaseRequested)
                .Then(context =>
                {
                    context.Instance.UserId = context.Data.UserId;
                    context.Instance.ItemId = context.Data.ItemId;
                    context.Instance.Quantity = context.Data.Quantity;
                    context.Instance.Received = DateTimeOffset.UtcNow;
                    context.Instance.LastUpdated = context.Instance.Received;
                    _logger.LogInformation(
                        "Calculating purchase total for ItemId: {ItemId} with Quantity: {Quantity}, " +
                        "CorrelationId: {CorrelationId}",
                        context.Instance.ItemId,
                        context.Instance.Quantity,
                        context.Instance.CorrelationId
                        );
                })
                .Activity(x => x.OfType<CalculatePurchaseTotalActivity>())
                .Send( context => new GrantItems(
                    context.Instance.UserId,
                    context.Instance.ItemId,
                    context.Instance.Quantity,
                    context.Instance.CorrelationId))
                .TransitionTo(Accepted)
                .Catch<Exception>(ex => ex.
                    Then(context => {
                        context.Instance.ErrorMessage = context.Exception.Message;
                        context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                        _logger.LogError( 
                            context.Exception, 
                            "Could not calculate the total price with Corelation Id: {CorrelationId}. " +
                            "Error: { ErrorMessage}",
                            context.Instance.CorrelationId,
                            context.Instance.ErrorMessage);
                    })
                    .TransitionTo(Faulted)
                // let client know
                    .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Instance))
                )
        );
    }

    private void ConfigureAccepted()
    {
        During(Accepted, 
            Ignore(PurchaseRequested),
            When(InventoryItemsGranted)
                .Then(context =>
                {
                    context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogInformation("Purchase Request with Correlation id: {CorrelationId} for User Id: {UserId}" +
                        " has been approved with {Quantity} items granted", 
                        context.Instance.CorrelationId,
                        context.Instance.UserId,
                        context.Instance.Quantity
                        );
                })
                .Send( context => new DebitGil(
                        context.Instance.UserId,
                        context.Instance.PurchaseTotal.Value,
                        context.Instance.CorrelationId
                        ))
                .TransitionTo(ItemsGranted),
                When(GrantItemsFaulted)
                .Then(context =>
                {
                    context.Instance.ErrorMessage = context.Data.Exceptions[0].Message;
                    context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogError( 
                        "Could not grant items for purchase with Correlation id {CorrelationId}. " +
                        "Error: {ErrorMessage}",
                        context.Instance.CorrelationId,
                        context.Instance.ErrorMessage);
                })
                .TransitionTo(Faulted)
                .ThenAsync(async context => await _messageHub.SendStatusAsync(context.Instance))
            );
    }

    private void ConfigureItemsGranted()
    {
        During(ItemsGranted, 
            Ignore(PurchaseRequested),
            Ignore(InventoryItemsGranted),
            When(GilDebited)
                .Then(context =>
                {
                    context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "Gil debited successfully for purchase with Correlation id: {CorrelationId}. " +
                        "Total amount: {PurchaseTotal}",
                        context.Instance.CorrelationId,
                        context.Instance.PurchaseTotal);
                }).TransitionTo(Completed)
                .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Instance)),
            When(DebitGilFaulted)
                .Send(context => new SubtractItems(
                    context.Instance.UserId,
                    context.Instance.ItemId,
                    context.Instance.Quantity,
                    context.Instance.CorrelationId
                    ))
                .Then(context =>
                {
                    context.Instance.ErrorMessage = context.Data.Exceptions[0].Message;
                    context.Instance.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogError(
                        "Could not debit gil for purchase with Correlation id {CorrelationId}. " +
                        "Error: {ErrorMessage}",
                        context.Instance.CorrelationId,
                        context.Instance.ErrorMessage);
                })
                .TransitionTo(Faulted)
                .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Instance))
            );
    }

    private void ConfigureCompleted()
    {
        During(Completed,
            Ignore(PurchaseRequested),
            Ignore(InventoryItemsGranted),
            Ignore(GilDebited));

    }
    private void ConfigureAny()
    {
        DuringAny(
            When(GetPurchaseState)
                .Respond( x=> x.Instance)
            );
    }

    private void ConfigureFaulted()
    {
        During(Faulted,
            Ignore(PurchaseRequested),
            Ignore(InventoryItemsGranted),
            Ignore(GilDebited));
    }
    
    
}