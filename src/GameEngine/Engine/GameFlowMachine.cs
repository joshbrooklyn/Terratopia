using Stateless;
using GameEngine.Enums;

namespace GameEngine.Engine;

public class GameFlowMachine
{
    private readonly StateMachine<GameFlowState, GameFlowTrigger> _machine;

    public GameFlowState CurrentState => _machine.State;

    public GameFlowMachine()
    {
        _machine = new StateMachine<GameFlowState, GameFlowTrigger>(GameFlowState.GameStarting);
        ConfigureMachine();
    }

    public void CompleteStartup()   => _machine.Fire(GameFlowTrigger.StartupComplete);
    public void StartSkirmish()     => _machine.Fire(GameFlowTrigger.StartSkirmish);
    public void EndSkirmish()       => _machine.Fire(GameFlowTrigger.SkirmishEnded);
    public void OpenDataBrowser()   => _machine.Fire(GameFlowTrigger.OpenDataBrowser);
    public void CloseDataBrowser()  => _machine.Fire(GameFlowTrigger.CloseDataBrowser);

    private void ConfigureMachine()
    {
        _machine.Configure(GameFlowState.GameStarting)
            .Permit(GameFlowTrigger.StartupComplete, GameFlowState.MainMenu);

        _machine.Configure(GameFlowState.MainMenu)
            .OnEntry(() => GameEventBus.RaiseStateChanged(GameFlowState.MainMenu))
            .Permit(GameFlowTrigger.StartSkirmish, GameFlowState.Skirmish)
            .Permit(GameFlowTrigger.OpenDataBrowser, GameFlowState.DataBrowser);

        _machine.Configure(GameFlowState.DataBrowser)
            .OnEntry(() => GameEventBus.RaiseStateChanged(GameFlowState.DataBrowser))
            .Permit(GameFlowTrigger.CloseDataBrowser, GameFlowState.MainMenu);

        _machine.Configure(GameFlowState.Skirmish)
            .OnEntry(() => GameEventBus.RaiseStateChanged(GameFlowState.Skirmish))
            .Permit(GameFlowTrigger.SkirmishEnded, GameFlowState.MainMenu);
    }
}
