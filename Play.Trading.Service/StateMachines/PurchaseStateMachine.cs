using Automatonymous;

namespace Play.Trading.Service.StateMachines;

public class PurchaseStateMachine : MassTransitStateMachine<PurchaseState>
{
    public State Accepted { get; }
    
    public State ItemsGranted { get; }
    
    public State Completed { get; }

    public State Faulted { get; set; }

    public PurchaseStateMachine()
    {
        InstanceState(state => state.CurrentState);
    }
}