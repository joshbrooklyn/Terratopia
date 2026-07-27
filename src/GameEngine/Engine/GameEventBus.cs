using GameEngine.Enums;

namespace GameEngine.Engine;

public static class GameEventBus
{
    public static event Action<GameFlowState>? StateChanged;

    public static void RaiseStateChanged(GameFlowState newState) => StateChanged?.Invoke(newState);

    public static void Reset() => StateChanged = null;
}
