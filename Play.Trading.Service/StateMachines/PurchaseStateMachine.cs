using System;
using Automatonymous;

namespace Play.Trading.Service.StateMachines;

public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
{
    public State Accepted { get; }
    
    public State ItemsGranted { get; }
    
    public State Completed { get; }

    public State Faulted { get; set; }

    // declare an event
    public Event<PurchaseRequested> PurchaseRequested { get;  }
    public PurchaseStateMachine()
    {
        InstanceState(state => state.CurrentState);
        ConfigureEvents();
    }

    private void ConfigureEvents()
    {
        Event(() => PurchaseRequested); 
    }

    private void ConfigureInitialState()
    {
        Initially(
            When(PurchaseRequested)
                .Then(context =>
                {
                    // data = Incoming event/message payload
                    // instance = current state machine instance
                    // (a persisted object often called a saga)
                    context.Instance.UserId = context.Data.UserId;
                    context.Instance.ItemId = context.Data.ItemId;
                    context.Instance.Quantity = context.Data.Quantity;
                    context.Instance.Received = DateTimeOffset.UtcNow;
                    context.Instance.LastUpdated = context.Instance.Received;
                })
                .TransitionTo(Accepted)
            );
    }
}