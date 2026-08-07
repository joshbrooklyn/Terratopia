using System.Reflection;
using System.Text.Json;
using CombatEngine.CombatFunctions;
using CombatEngine.DataClasses;
using CombatEngine.Passives;
using GameEngine.DataClasses;

namespace Terratopia.Tests.CombatEngine.Internal;

[Collection("CombatEngineSerial")]
public class CombatFunctionRegistryTests
{
    // The three action schemas that carry combatFunction/parameters. Each is a hand-maintained
    // mirror of the C# side, so every one of them gets checked for drift.
    private static readonly string[] ActionSchemaResources =
    [
        Tech.SchemaResourceName,
        Item.SchemaResourceName,
        MonsterAction.SchemaResourceName,
    ];

    private static JsonElement LoadSchema(string resourceName)
    {
        using var stream = Assembly.GetAssembly(typeof(Tech))!.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' not found.");
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    // Mirrors JsonNamingPolicy.CamelCase, which is how ContentLoader maps these property names.
    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    // ---------------------------------------------------------------
    // Registry resolution
    // ---------------------------------------------------------------

    [Fact]
    public void CombatFunctionRegistry_ResolvesRegisteredFunctionsByName()
    {
        // What: verifies the registry maps each registered FunctionName string — the exact value
        //       that appears in GameData JSON — back to its implementing class.
        // How:  Resolve is called with each of the three shipped names and the concrete runtime
        //       type of the returned instance is asserted. This is the whole point of the
        //       refactor: a name in data selects the class that resolves the action, so if a
        //       FunctionName const and its registry entry ever disagree, this fails.
        Assert.IsType<BasicDamageFunction>(CombatFunctionRegistry.Resolve(BasicDamageFunction.FunctionName));
        Assert.IsType<BasicHealFunction>(CombatFunctionRegistry.Resolve(BasicHealFunction.FunctionName));
        Assert.IsType<NoOpFunction>(CombatFunctionRegistry.Resolve(NoOpFunction.FunctionName));
    }

    [Fact]
    public void CombatFunctionRegistry_ThrowsOnUnknownName()
    {
        // What: verifies an unregistered name is a hard failure rather than a silent no-op.
        // How:  Resolve is called with a name that is deliberately not in the registry. This is
        //       the explicit contrast with PowerKeywordRegistry, which drops unknown keyword
        //       names silently — that is tolerable for a keyword (an action just loses a bonus)
        //       but not for a CombatFunction, which IS the action: dropping it would turn a Tech
        //       into a do-nothing instead of surfacing the typo. The message is asserted to name
        //       the offending value so the failure points at the bad data file.
        var ex = Assert.Throws<InvalidOperationException>(() => CombatFunctionRegistry.Resolve("NotAFunction"));
        Assert.Contains("NotAFunction", ex.Message);
    }

    // ---------------------------------------------------------------
    // Hand-synced schema surfaces — drift guards
    // ---------------------------------------------------------------

    [Fact]
    public void CombatFunctionRegistry_MatchesSchemaEnum()
    {
        // What: verifies the combatFunction enum in every action schema lists exactly the
        //       functions the registry actually has registered.
        // How:  The schemas are hand-maintained against CombatFunctionRegistry (the same
        //       arrangement the keywords enum has), so they can silently drift: a new function
        //       that never reaches the schema is unauthorable, and a schema entry with no
        //       registered class passes load-time validation only to throw mid-combat. This
        //       reads each embedded schema resource, pulls properties.combatFunction.enum, and
        //       asserts it is set-equal to CombatFunctionRegistry.RegisteredNames.
        var registered = CombatFunctionRegistry.RegisteredNames.OrderBy(n => n).ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaEnum = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("combatFunction").GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n).ToArray();

            Assert.Equal(registered, schemaEnum);
        }
    }

    [Fact]
    public void CombatFunctionParameters_MatchesSchemaSuperset()
    {
        // What: verifies the parameters block in every action schema declares exactly the fields
        //       CombatFunctionParameters exposes — no more, no less.
        // How:  parameters is a closed (additionalProperties: false) hand-maintained superset, so
        //       a field present in C# but missing from the schema is unauthorable, and a field in
        //       the schema with no C# property is silently discarded on deserialization. This
        //       reflects over CombatFunctionParameters, camelCases each property name the way
        //       ContentLoader's JsonNamingPolicy.CamelCase does, and asserts set equality against
        //       properties.parameters.properties in each schema.
        var expected = typeof(CombatFunctionParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => CamelCase(p.Name))
            .OrderBy(n => n)
            .ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaFields = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("parameters").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

            Assert.Equal(expected, schemaFields);
        }
    }

    [Fact]
    public void BuffDebuffSpec_MatchesSchemaSuperset()
    {
        // What: verifies parameters.buffsDebuffs.items in every action schema declares exactly the
        //       fields BuffDebuffSpec exposes — the same hand-mirror guarantee as
        //       CombatFunctionParameters_MatchesSchemaSuperset, one level deeper.
        // How:  buffsDebuffs.items is a closed (additionalProperties: false) hand-maintained
        //       mirror of BuffDebuffSpec, so this reflects over it, camelCases each property name,
        //       and asserts set equality against parameters.properties.buffsDebuffs.items.properties
        //       in each schema.
        var expected = typeof(BuffDebuffSpec)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => CamelCase(p.Name))
            .OrderBy(n => n)
            .ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaFields = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("parameters").GetProperty("properties")
                .GetProperty("buffsDebuffs").GetProperty("items").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

            Assert.Equal(expected, schemaFields);
        }
    }

    [Fact]
    public void RegenDrainSpec_MatchesSchemaSuperset()
    {
        // What: verifies parameters.regensDrains.items in every action schema declares exactly the
        //       fields RegenDrainSpec exposes — the same hand-mirror guarantee as
        //       BuffDebuffSpec_MatchesSchemaSuperset.
        // How:  regensDrains.items is a closed (additionalProperties: false) hand-maintained mirror
        //       of RegenDrainSpec, so this reflects over it, camelCases each property name, and
        //       asserts set equality against parameters.properties.regensDrains.items.properties in
        //       each schema.
        var expected = typeof(RegenDrainSpec)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => CamelCase(p.Name))
            .OrderBy(n => n)
            .ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaFields = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("parameters").GetProperty("properties")
                .GetProperty("regensDrains").GetProperty("items").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

            Assert.Equal(expected, schemaFields);
        }
    }

    [Fact]
    public void PassiveApplySpec_MatchesSchemaSuperset()
    {
        // What: verifies parameters.passivesApplied.items in every action schema declares exactly
        //       the fields PassiveApplySpec exposes - the same hand-mirror guarantee as
        //       BuffDebuffSpec_MatchesSchemaSuperset / RegenDrainSpec_MatchesSchemaSuperset.
        // How:  passivesApplied.items is a closed (additionalProperties: false) hand-maintained
        //       mirror of PassiveApplySpec, so this reflects over it, camelCases each property
        //       name, and asserts set equality against
        //       parameters.properties.passivesApplied.items.properties in each schema.
        var expected = typeof(PassiveApplySpec)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => CamelCase(p.Name))
            .OrderBy(n => n)
            .ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaFields = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("parameters").GetProperty("properties")
                .GetProperty("passivesApplied").GetProperty("items").GetProperty("properties")
                .EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

            Assert.Equal(expected, schemaFields);
        }
    }

    [Fact]
    public void PassiveRegistry_MatchesSchemaEnum()
    {
        // What: verifies the "passive" enum inside parameters.passivesApplied.items in every
        //       action schema (and monster.schema.json's "passives" array) lists exactly the
        //       passives PassiveRegistry actually has registered - the same drift guard
        //       CombatFunctionRegistry_MatchesSchemaEnum runs for combatFunction.
        // How:  reads properties.parameters.properties.passivesApplied.items.properties.passive.enum
        //       from each action schema, plus properties.passives.items.enum from
        //       monster.schema.json, and asserts each is set-equal to
        //       PassiveRegistry.RegisteredNames.
        var registered = PassiveRegistry.RegisteredNames.OrderBy(n => n).ToArray();

        foreach (var resourceName in ActionSchemaResources)
        {
            var schemaEnum = LoadSchema(resourceName)
                .GetProperty("properties").GetProperty("parameters").GetProperty("properties")
                .GetProperty("passivesApplied").GetProperty("items").GetProperty("properties")
                .GetProperty("passive").GetProperty("enum")
                .EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n).ToArray();

            Assert.Equal(registered, schemaEnum);
        }

        var monsterPassiveEnum = LoadSchema("GameEngine.Schemas.monster.schema.json")
            .GetProperty("properties").GetProperty("passives").GetProperty("items").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n).ToArray();

        Assert.Equal(registered, monsterPassiveEnum);
    }
}
