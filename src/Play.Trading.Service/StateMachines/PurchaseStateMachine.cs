using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Play.Common;
using Play.Common.Settings;
using Play.Identity.Contracts;
using Play.Inventory.Contracts;
using Play.Trading.Service.Activities;
using Play.Trading.Service.Entities;
using Play.Trading.Service.SignalR;

namespace Play.Trading.Service.StateMachines;

public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
{
    private readonly MessageHub _messageHub;
    private readonly ILogger<PurchaseStateMachine> _logger;
    private readonly IRepository<UserPurchaseStats> _userPurchaseStatsRepository;
    private readonly Counter<int> purchaseStartedCounter;
    private readonly Counter<int> purchaseSuccessCounter;
    private readonly Counter<int> purchaseFailedCounter;
    private readonly Counter<int> purchasesFlaggedCounter;
    private readonly Histogram<double> purchaseDurationSeconds;

    private const long MinSamplesForAnomalyCheck = 5;
    private const double AnomalyZScoreThreshold = 3.0;
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
        IRepository<UserPurchaseStats> userPurchaseStatsRepository,
        ILogger<PurchaseStateMachine> logger)
    {
        _messageHub = messageHub;
        _userPurchaseStatsRepository = userPurchaseStatsRepository;
        _logger = logger;

        var settings = configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
        Meter meter = new(settings.ServiceName);
        purchaseStartedCounter = meter.CreateCounter<int>("purchase_started");
        purchaseSuccessCounter = meter.CreateCounter<int>("purchase_success");
        purchaseFailedCounter = meter.CreateCounter<int>("purchase_failed");
        purchasesFlaggedCounter = meter.CreateCounter<int>("purchases_flagged_total");
        purchaseDurationSeconds = meter.CreateHistogram<double>("purchase_duration_seconds");
        
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
        var start = Stopwatch.StartNew();
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
                        purchaseDurationSeconds.Record(
                            (DateTimeOffset.UtcNow - context.Saga.Received).TotalSeconds,
                            new KeyValuePair<string, object>("outcome", "failure"));
                    })
                    .TransitionTo(Faulted)
                // let client know
                    .ThenAsync( async context => await _messageHub.SendStatusAsync(context.Saga))
                )
        );
        
        var end = start.ElapsedMilliseconds;
        _logger.LogInformation("PurchaseStateMachine initialized in {ElapsedMilliseconds} ms", end);
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
                    purchaseFailedCounter.Add(1,
                        new KeyValuePair<string, object>(nameof(context.Saga.ItemId),
                                                         context.Saga.ItemId));
                    purchaseDurationSeconds.Record(
                        (DateTimeOffset.UtcNow - context.Saga.Received).TotalSeconds,
                        new KeyValuePair<string, object>("outcome", "failure"));
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
                    purchaseDurationSeconds.Record(
                        (DateTimeOffset.UtcNow - context.Saga.Received).TotalSeconds,
                        new KeyValuePair<string, object>("outcome", "success"));
                })
                .ThenAsync(async context => await CheckAndUpdatePurchaseStatsAsync(context))
                .TransitionTo(Completed)
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
                    purchaseFailedCounter.Add(1,
                        new KeyValuePair<string, object>(nameof(context.Saga.ItemId),
                                                         context.Saga.ItemId));
                    purchaseDurationSeconds.Record(
                        (DateTimeOffset.UtcNow - context.Saga.Received).TotalSeconds,
                        new KeyValuePair<string, object>("outcome", "failure"));
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

    private async Task CheckAndUpdatePurchaseStatsAsync(BehaviorContext<PurchaseState, GilDebited> context)
    {
        var userId = context.Saga.UserId;
        var total = (double)context.Saga.PurchaseTotal.GetValueOrDefault();

        var stats = await _userPurchaseStatsRepository.GetAsync(userId);
        var isNew = stats == null;
        if (isNew)
        {
            stats = new UserPurchaseStats { Id = userId };
        }
        else if (stats.SampleCount >= MinSamplesForAnomalyCheck)
        {
            var stdDev = Math.Sqrt(stats.M2 / (stats.SampleCount - 1));
            if (stdDev > 0 && Math.Abs(total - stats.Mean) / stdDev > AnomalyZScoreThreshold)
            {
                _logger.LogWarning(
                    "Purchase flagged as statistical outlier for UserId: {UserId}, CorrelationId: {CorrelationId}. " +
                    "PurchaseTotal: {PurchaseTotal}, HistoricalMean: {Mean}, StdDev: {StdDev}",
                    userId, context.Saga.CorrelationId, total, stats.Mean, stdDev);
                purchasesFlaggedCounter.Add(1, new KeyValuePair<string, object>("reason", "price_outlier"));
            }
        }

        // Welford update — must happen AFTER the check above, using the prior mean/M2,
        // or the current sample dampens its own anomaly signal.
        stats.SampleCount++;
        var delta = total - stats.Mean;
        stats.Mean += delta / stats.SampleCount;
        stats.M2 += delta * (total - stats.Mean);
        stats.LastUpdated = DateTimeOffset.UtcNow;

        if (isNew)
        {
            await _userPurchaseStatsRepository.CreateAsync(stats);
        }
        else
        {
            await _userPurchaseStatsRepository.UpdateAsync(stats);
        }
    }
}