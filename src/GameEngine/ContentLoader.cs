using System.Text.Json;
using System.Text.Json.Serialization;
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
        var path = Path.Combine(FindGameDataPath(), "Techs", "techs.json");
        var techs = JsonSerializer.Deserialize<List<Tech>>(File.ReadAllText(path), _options)
            ?? throw new InvalidOperationException("techs.json deserialized to null.");

        foreach (var tech in techs)
        {
            bool mustBeBlank = tech.TargetingType is TargetingType.All or TargetingType.Self;

            if (mustBeBlank && tech.AllowMultipleAttackOnSameTarget is not null)
                throw new ArgumentException(
                    $"Tech '{tech.TechId}': allowMultipleAttackOnSameTarget must be blank for TargetingType '{tech.TargetingType}'.");
        }

        return techs;
    }

    public static List<Item> LoadItems()
    {
        var path = Path.Combine(FindGameDataPath(), "Items", "items.json");
        return JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(path), _options)
            ?? throw new InvalidOperationException("items.json deserialized to null.");
    }

    public static List<Monster> LoadMonsters()
    {
        var path = Path.Combine(FindGameDataPath(), "Monsters", "monsters.json");
        return JsonSerializer.Deserialize<List<Monster>>(File.ReadAllText(path), _options)
            ?? throw new InvalidOperationException("monsters.json deserialized to null.");
    }

    public static List<MonsterAction> LoadMonsterActions()
    {
        var path = Path.Combine(FindGameDataPath(), "MonsterActions", "monsterActions.json");
        return JsonSerializer.Deserialize<List<MonsterAction>>(File.ReadAllText(path), _options)
            ?? throw new InvalidOperationException("monsterActions.json deserialized to null.");
    }

    public static List<Dungeon> LoadDungeons()
    {
        var path = Path.Combine(FindGameDataPath(), "Dungeons", "dungeons.json");
        return JsonSerializer.Deserialize<List<Dungeon>>(File.ReadAllText(path), _options)
            ?? throw new InvalidOperationException("dungeons.json deserialized to null.");
    }

    public static List<Adventurer> LoadAdventurers()
    {
        var path = Path.Combine(FindGameDataPath(), "Adventurers", "adventurers.json");
        return JsonSerializer.Deserialize<List<Adventurer>>(File.ReadAllText(path), _options)
            ?? throw new InvalidOperationException("adventurers.json deserialized to null.");
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
