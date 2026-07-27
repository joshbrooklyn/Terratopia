using Godot;
using CombatEngine.Engine;

public partial class CombatEngineAutoload : Node
{
    public static CombatEngineClass Engine => CombatEngineClass.Instance;

    public override void _Ready()
    {
        _ = CombatEngineClass.Instance;
    }
}
