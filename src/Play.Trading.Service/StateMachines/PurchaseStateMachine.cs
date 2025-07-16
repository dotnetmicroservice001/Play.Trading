using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Play.Common.Settings;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service.Activities;
using Play.Trading.Service.SignalR;

namespace Play.Trading.Service.StateMachines;

public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
{
    private readonly MessageHub _messageHub;
    private readonly ILogger<PurchaseStateMachine> _logger;
    private readonly Counter<int> purchaseStartedCounter; 
    private readonly Counter<int> purchaseSuccessCounter;
    private readonly Counter<int> purchaseFailedCounter;
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
    
    
    public PurchaseStateMachine(
        IConfiguration configuration,
        MessageHub messageHub, 
        ILogger<PurchaseStateMachine> logger)
    {
        _messageHub = messageHub;
        _logger = logger;
        
        var settings = configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
        Meter meter = new(settings.ServiceName);
        purchaseStartedCounter = meter.CreateCounter<int>("purchase_started");
        purchaseSuccessCounter = meter.CreateCounter<int>("purchase_success");
        purchaseFailedCounter = meter.CreateCounter<int>("purchase_failed");
        
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
                    context.Saga.UserId = context.Message.UserId;
                    context.Saga.ItemId = context.Message.ItemId;
                    context.Saga.Quantity = context.Message.Quantity;
                    context.Saga.Received = DateTimeOffset.UtcNow;
                    context.Saga.LastUpdated = context.Saga.Received;
                    _logger.LogInformation(
                        "Calculating purchase total for ItemId: {ItemId} with Quantity: {Quantity}, " +
                        "CorrelationId: {CorrelationId}",
                        context.Saga.ItemId,
                        context.Saga.Quantity,
                        context.Saga.CorrelationId
                        );
                    purchaseStartedCounter.Add(1, 
                        new KeyValuePair<string, object>(nameof(context.Saga.ItemId),
                                                         context.Saga.ItemId));
                })
                .Activity(x => x.OfType<CalculatePurchaseTotalActivity>())
                .Send( context => new GrantItems(
                    context.Saga.UserId,
                    context.Saga.ItemId,
                    context.Saga.Quantity,
                    context.Saga.CorrelationId))
                .TransitionTo(Accepted)
                .Catch<Exception>(ex => ex.
                    Then(context => {
                        context.Saga.ErrorMessage = context.Exception.Message;
                        context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                        _logger.LogError( 
                            context.Exception, 
                            "Could not calculate the total price with Corelation Id: {CorrelationId}. " +
                            "Error: { ErrorMessage}",
                            context.Saga.CorrelationId,
                            context.Saga.ErrorMessage);
                        purchaseFailedCounter.Add(1, 
                            new KeyValuePair<string, object>(nameof(context.Saga.ItemId),
                                                             context.Saga.ItemId));
                    })
                    .TransitionTo(Faulted)
                // let client know
                    .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Saga))
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
                    context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogInformation("Purchase Request with Correlation id: {CorrelationId} for User Id: {UserId}" +
                        " has been approved with {Quantity} items granted", 
                        context.Saga.CorrelationId,
                        context.Saga.UserId,
                        context.Saga.Quantity
                        );
                })
                .Send( context => new DebitGil(
                        context.Saga.UserId,
                        context.Saga.PurchaseTotal.Value,
                        context.Saga.CorrelationId
                        ))
                .TransitionTo(ItemsGranted),
                When(GrantItemsFaulted)
                .Then(context =>
                {
                    context.Saga.ErrorMessage = context.Message.Exceptions[0].Message;
                    context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogError( 
                        "Could not grant items for purchase with Correlation id {CorrelationId}. " +
                        "Error: {ErrorMessage}",
                        context.Saga.CorrelationId,
                        context.Saga.ErrorMessage);
                })
                .TransitionTo(Faulted)
                .ThenAsync(async context => await _messageHub.SendStatusAsync(context.Saga))
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
                    context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogInformation(
                        "Gil debited successfully for purchase with Correlation id: {CorrelationId}. " +
                        "Total amount: {PurchaseTotal}",
                        context.Saga.CorrelationId,
                        context.Saga.PurchaseTotal);
                    purchaseSuccessCounter.Add(1, 
                        new KeyValuePair<string, object>(nameof(context.Saga.ItemId),
                                                         context.Saga.ItemId));
                }).TransitionTo(Completed)
                .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Saga)),
            When(DebitGilFaulted)
                .Send(context => new SubtractItems(
                    context.Saga.UserId,
                    context.Saga.ItemId,
                    context.Saga.Quantity,
                    context.Saga.CorrelationId
                    ))
                .Then(context =>
                {
                    context.Saga.ErrorMessage = context.Message.Exceptions[0].Message;
                    context.Saga.LastUpdated = DateTimeOffset.UtcNow;
                    _logger.LogError(
                        "Could not debit gil for purchase with Correlation id {CorrelationId}. " +
                        "Error: {ErrorMessage}",
                        context.Saga.CorrelationId,
                        context.Saga.ErrorMessage);
                })
                .TransitionTo(Faulted)
                .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Saga))
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
                .Respond( x=> x.Saga)
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