using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using CombatEngine.Enums;
using GameEngine.DataClasses;

namespace GameEngine;

public static class ContentLoader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() },
    };

    public static List<Tech> LoadTechs()
    {
        var techs = LoadDirectory<Tech>("Techs");

        foreach (var tech in techs)
        {
            bool mustBeBlank = tech.TargetingType is TargetingType.All or TargetingType.Self;

            if (mustBeBlank && tech.AllowMultipleAttackOnSameTarget is not null)
                throw new ArgumentException(
                    $"Tech '{tech.TechId}': allowMultipleAttackOnSameTarget must be blank for TargetingType '{tech.TargetingType}'.");
        }

        return techs;
    }

    public static List<Item> LoadItems() => LoadDirectory<Item>("Items");

    public static List<Monster> LoadMonsters() => LoadDirectory<Monster>("Monsters");

    public static List<MonsterAction> LoadMonsterActions() => LoadDirectory<MonsterAction>("MonsterActions");

    public static List<Dungeon> LoadDungeons() => LoadDirectory<Dungeon>("Dungeons");

    public static List<Adventurer> LoadAdventurers() => LoadDirectory<Adventurer>("Adventurers");

    private static List<T> LoadDirectory<T>(string subfolder)
    {
        var dir = Path.Combine(FindGameDataPath(), subfolder);
        var files = Directory.EnumerateFiles(dir, "*.json")
            .Where(f => !Path.GetFileName(f).Equals("NotImplemented.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        var result = new List<T>();
        foreach (var file in files)
        {
            var item = JsonSerializer.Deserialize<T>(File.ReadAllText(file), _options)
                ?? throw new InvalidOperationException($"{Path.GetFileName(file)} deserialized to null.");
            result.Add(item);
        }

        return result;
    }

    private static string FindGameDataPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "GameData");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"GameData directory not found walking up from {AppContext.BaseDirectory}");
    }
}
