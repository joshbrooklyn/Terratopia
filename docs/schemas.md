# JSON Schemas

Reference for how GameData JSON files are defined and validated.

## Overview

Every GameData category — Adventurers, Monsters, Techs, Items, MonsterActions, Dungeons — has a corresponding [JSON Schema](https://json-schema.org/) (draft-07) that defines its exact shape. Schemas are strict, not permissive: every one sets `additionalProperties: false` and an explicit `required` list, so an unexpected or missing field is a validation error, not a silent no-op.

Two independent systems enforce these schemas: VS Code editing (the GameData Editor extension) and the C# game engine at load time. Both read from the same six files, but neither reads them from the same place — see "Canonical location & propagation" below.

This is a related but separate topic from **[Schema Migrations](schema-migrations.md)**: this page covers what a schema *is* and how it's validated; that page covers what happens when a schema changes and existing data files fall behind.

## Canonical location & propagation

The canonical schemas live in `src/GameEngine/Schemas/`: `adventurer.schema.json`, `dungeon.schema.json`, `item.schema.json`, `monster.schema.json`, `monsteraction.schema.json`, `tech.schema.json`. There are two independent consumers of these files, neither of which reads this folder directly:

1. **C# runtime** — the schemas are embedded resources in the GameEngine assembly (`GameEngine.csproj`: `<EmbeddedResource Include="Schemas\*.schema.json" />`), picked up automatically on every C# build.
2. **GameData Editor (VS Code extension)** — `src/GameDataEditor/scripts/copy-schemas.js` copies every `*.schema.json` from `src/GameEngine/Schemas` into `src/GameDataEditor/schemas/`. This runs as part of `npm run compile` / `npm run watch` (`package.json`: `"compile": "npm run copy-schemas && tsc -p ./"`). The extension only ever reads its own copied folder — never `src/GameEngine/Schemas` directly.

**Editing a schema means editing the copy in `src/GameEngine/Schemas/`, then running `npm run copy-schemas` (in `GameDataEditor`) to propagate it to the extension.** The embedded-resource copy needs no separate step — it's picked up on the next C# build.

## Common schema conventions

```json
"schemaVersion": {
  "type": "integer",
  "const": 2
}
```

- `schemaVersion` is a version pin (`const`, not a range) commented "Managed by the GameData Editor — do not hand-edit." Every valid data file must declare this exact number. See [Schema Migrations](schema-migrations.md) for what happens when this number changes.
- Closed-vocabulary fields use `enum`: `targetingType` (`Choose`/`Random`/`All`/`Self`), `validTargets` (`Allies`/`Enemies`/`Both`), `livingOrDead` (`Living`/`Dead`/`Both`), `directEffects[].effectType`/`element`/`calcType`.
- Two enums are **hand-maintained against C# registries, not generated**: `keywords` (must stay in sync with `PowerKeywordRegistry.cs` — see [Keywords](keywords.md)) and `monster.passives` (must stay in sync with `PassiveRegistry.cs` — see [Passives](passives.md)). This used to be code-generated for passives (a since-deleted `generate-passive-enum.js` script, driven by an npm script `generate-schemas`) but was reverted to hand-maintenance — if you go looking for that script and can't find it, that's why.
- Cross-file references — `monster.monsterActionIds`, `adventurer.techsIds`/`itemIds`, `dungeon.monsterIds` — are plain string arrays that reference another file's id field *by convention only*. There's no `$ref` and no schema-level referential integrity; a dangling id fails silently at whatever point downstream code tries to resolve it, not at schema validation.

## Consumption path 1 — VS Code editor

- `package.json`'s `contributes.jsonValidation` binds file globs (`/Adventurers/*.json`, `/Monsters/*.json`, `/Techs/*.json`, `/Items/*.json`, `/MonsterActions/*.json`) directly to the copied schema files, giving native inline validation on any GameData JSON file opened in VS Code — independent of the custom Form Editor below.
- `gameDataLoader.ts` — `loadSchemaByFileName`/`loadSchemaForCategory` read and cache a schema from the copied `schemas/` folder. `getSchemaVersion(schema)` reads `properties.schemaVersion.const`, so nothing else needs to hardcode the current version number:
  ```ts
  export function getSchemaVersion(schema: JsonSchemaObject): number {
      const version = schema.properties.schemaVersion?.const;
      if (typeof version !== 'number') {
          throw new Error('Schema is missing a numeric schemaVersion.const.');
      }
      return version;
  }
  ```
  `CATEGORY_DEFINITIONS` maps each Form-Editor category to its folder, id/name fields, and schema file name. Note it only covers Adventurers, Monsters, Techs, and Items — MonsterActions has no Form Editor category (only native `jsonValidation`).
- `formEditorPanel.ts` — loads the category's schema via `getSchemaWithDynamicEnums`, which patches specific array fields' `enum` with live ids collected from other GameData files (`DYNAMIC_ENUM_FIELDS`; e.g. an Adventurer's `techsIds`/`itemIds` fields get their `enum` populated from whatever Techs/Items files actually exist). The resulting schema and the file's data are posted to the webview (`media/main.js`), which renders the editing form, and validated again on save via `validateAgainstSchema` (`schemaValidation.ts`) before the file is written to disk.

## Consumption path 2 — C# runtime (`ContentLoader.cs`)

`src/GameEngine/ContentLoader.cs` does real validate-then-deserialize using `JsonSchema.Net`:

```csharp
private static JsonSchema GetSchema<T>() where T : IGameDataObject
{
    var resourceName = T.SchemaResourceName;
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded schema resource '{resourceName}' not found.");
    var schema = JsonSchema.FromText(new StreamReader(stream).ReadToEnd());
    _schemaCache[typeof(T)] = schema;
    return schema;
}
```

Each data class implements `IGameDataObject.SchemaResourceName` (a static abstract property) naming its embedded schema resource. `LoadDirectory<T>` — the shared implementation behind `LoadTechs`, `LoadItems`, `LoadMonsters`, `LoadMonsterActions`, `LoadDungeons`, and `LoadAdventurers` — parses each file in the category's folder, calls `schema.Evaluate(...)`, and throws `InvalidOperationException` (with every validation error's instance path and message) if it fails. Only then does it deserialize via `System.Text.Json` (camelCase naming, `JsonStringEnumConverter`).

This means an invalid or out-of-sync GameData file fails to *load* at runtime, not just fails editor lint — schema drift is a hard failure in-game, not a cosmetic warning.

One invariant isn't expressible in the schema and is enforced by hand instead: `LoadTechs()` additionally requires `allowMultipleAttackOnSameTarget` to be null whenever `targetingType` is `All` or `Self`.

Both the C# loader and the TS extension read the GameData folder location from the same environment variable, `TerratopiaGameDataPath` (`ContentLoader.FindGameDataPath()` / `gameDataLoader.ts`'s `findGameDataRoot()`).

## Extending: adding or changing a field

1. Edit the schema in `src/GameEngine/Schemas/` (the canonical copy).
2. Run `npm run copy-schemas` in `GameDataEditor` to propagate the change to the extension's copy. The embedded-resource copy the C# engine uses needs no separate step — it's picked up on the next build.
3. If the change would make existing GameData files invalid (a new required field, a narrowed enum, etc.), bump `schemaVersion.const` and see [Schema Migrations](schema-migrations.md) for how to write a migration step so old files aren't just left broken.
