using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using CombatEngine.Enums;
using GameEngine.DataClasses;
using Json.Schema;

namespace GameEngine;

public static class ContentLoader
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter() },
    };

    private static readonly Dictionary<Type, JsonSchema> _schemaCache = new();

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

    private static List<T> LoadDirectory<T>(string subfolder) where T : IGameDataObject
    {
        var schema = GetSchema<T>();
        var dir = Path.Combine(FindGameDataPath(), subfolder);
        var files = Directory.EnumerateFiles(dir, "*.json")
            .Where(f => !Path.GetFileName(f).Equals("NotImplemented.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        var result = new List<T>();
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));

            var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            if (!evaluation.IsValid)
            {
                var errors = new[] { evaluation }.Concat(evaluation.Details ?? Enumerable.Empty<EvaluationResults>())
                    .Where(d => d.Errors is { Count: > 0 })
                    .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)} ({subfolder}) failed schema validation:\n{string.Join("\n", errors)}");
            }

            var item = document.RootElement.Deserialize<T>(_options)
                ?? throw new InvalidOperationException($"{Path.GetFileName(file)} deserialized to null.");

            result.Add(item);
        }

        return result;
    }

    private static JsonSchema GetSchema<T>() where T : IGameDataObject
    {
        if (_schemaCache.TryGetValue(typeof(T), out var cached))
            return cached;

        var resourceName = T.SchemaResourceName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema resource '{resourceName}' not found.");

        var schema = JsonSchema.FromText(new StreamReader(stream).ReadToEnd());
        _schemaCache[typeof(T)] = schema;
        return schema;
    }

    private static string FindGameDataPath()
    {
        var path = Environment.GetEnvironmentVariable("TerratopiaGameDataPath");
        if (string.IsNullOrEmpty(path))
            throw new InvalidOperationException(
                "Environment variable 'TerratopiaGameDataPath' is not set.");
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"GameData directory not found at path from TerratopiaGameDataPath: {path}");
        return path;
    }
}
