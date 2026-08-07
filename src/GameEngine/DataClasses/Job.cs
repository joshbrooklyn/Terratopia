
namespace GameEngine.DataClasses;

public class Job : IGameDataObject
{
    public static string SchemaResourceName => "GameEngine.Schemas.job.schema.json";

    public string Id => JobId;
    public required int SchemaVersion { get; init; }
    public string JobId { get; init; } = "";
    public string Name { get; init; } = "";

    // Base stats. The level-0 floor of the formula: stat = base + level * growth.
    public int HpBase { get; init; }
    public int TpBase { get; init; }
    public int PowerBase { get; init; }
    public int DefenseBase { get; init; }
    public int SpeedBase { get; init; }

    // Per-level stat growth. Applied including level 1, i.e. stat = base + level * growth.
    public int HpPerLevel { get; init; }
    public int TpPerLevel { get; init; }
    public int PowerPerLevel { get; init; }
    public int DefensePerLevel { get; init; }
    public int SpeedPerLevel { get; init; }
}
