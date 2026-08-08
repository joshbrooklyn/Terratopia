# Schema Migrations

Reference for the `schemaVersion` field and the GameData Editor's "Scan & Migrate" tool.

## Overview

Schemas change over time — a field gets added, an enum gets tightened — and existing GameData files can drift out of validity as a result. `schemaVersion` and the migration tool in `src/GameDataEditor/src/migrate.ts` / `migrations.ts` exist to detect that drift and, where a fix has been written, apply it automatically. It's a manual, on-demand VS Code command, not something that runs automatically on file open or save.

This is a related but separate topic from **[JSON Schemas](schemas.md)**: that page covers what a schema is and how it's validated; this page covers what happens when a schema's requirements change out from under existing data.

## Versioning scheme

- Every schema declares a target version: `schemaVersion: { type: "integer", const: N }` (see [JSON Schemas](schemas.md)).
- Every GameData JSON file carries its own `schemaVersion` integer field. A file is "current" when its value matches the schema's `const`.
- `getSchemaVersion(schema)` (`gameDataLoader.ts`) is the only place that should ever read the target version — nothing else hardcodes it.
- The Form Editor (`formEditorPanel.ts`) always stamps **new or duplicated** entries with the current target version. It does not migrate existing data — only the migration tool described below does that.

## Migration step shape (`migrations.ts`)

```ts
export interface MigrationResult {
	notes: string[];
}

export type MigrationStep = (data: Record<string, unknown>, schema: JsonSchemaObject) => MigrationResult;

const MIGRATIONS: Record<string, Record<number, MigrationStep>> = {
	'item.schema.json': { 1: stripInvalidKeywords },
	'tech.schema.json': { 1: stripInvalidKeywords },
	'monsteraction.schema.json': { 1: stripInvalidKeywords },
	'adventurer.schema.json': { 1: addJobId, 2: dropAdventurerBaseStatsV3 },
	'job.schema.json': { 1: addJobBaseStatsV2 },
};
```

`MIGRATIONS` is keyed by **schema file name**, then by the version being migrated **from**. A `MigrationStep` is responsible both for transforming `data` in place and for advancing `data.schemaVersion` itself — the runner doesn't do that part for you.

```ts
export function runMigrations(schemaFileName: string, schema: JsonSchemaObject, data: Record<string, unknown>): RunMigrationsResult {
	const targetVersion = getSchemaVersion(schema);
	let currentVersion = typeof data.schemaVersion === 'number' ? data.schemaVersion : 0;
	const stepsForSchema = MIGRATIONS[schemaFileName] ?? {};

	while (currentVersion < targetVersion) {
		const step = stepsForSchema[currentVersion];
		if (!step) return { notes, incomplete: true };

		const result = step(data, schema);
		const nextVersion = typeof data.schemaVersion === 'number' ? data.schemaVersion : currentVersion;
		if (nextVersion <= currentVersion) return { notes, incomplete: true };
		currentVersion = nextVersion;
	}
	return { notes, incomplete: false };
}
```

`runMigrations` walks forward one version at a time, looking up a step for the current version each time. Two things cause it to stop early and report `incomplete: true`:
- **No step registered** for the current version — a version gap with no automatic fix.
- **A step ran but didn't advance `schemaVersion`** — a safety guard against an infinite loop from a buggy step.

**Currently two steps exist**: `stripInvalidKeywords`, registered for `item`, `tech`, and `monsteraction` schemas at version 1 (migrating 1→2). It removes any `keywords` entries not present in the schema's current `keywords.items.enum` (see [Keywords](keywords.md)), records a note per removed entry, and sets `data.schemaVersion = 2`. `addJobId`, registered for `adventurer` at version 1 (migrating 1→2), backfills the new required `jobId` field with `"fighter"` and sets `data.schemaVersion = 2` — a note tells the author to reassign each adventurer's job by hand.

Base stats were later moved from Adventurer to Job: `dropAdventurerBaseStatsV3` (adventurer 2→3) removes `maxHp`/`hp`/`maxTp`/`tp`/`power`/`defense`/`speed`, and `addJobBaseStatsV2` (job 1→2) backfills `hpBase`/`tpBase`/`powerBase`/`defenseBase`/`speedBase` to `80`/`50`/`10`/`10`/`10` — a note tells the author to tune per job by hand.

"Passive" was later renamed to "TriggeredEffect" throughout the system (see [Triggered Effects](triggered-effects.md)): `renamePassivesAppliedToV14`/`V15`/`V16` (tech 13→14, item 14→15, monsteraction 15→16) rename `parameters.passivesApplied` to `triggeredEffectsApplied` and each entry's `passive` field to `triggeredEffect`, and `renameMonsterPassivesToV2` (monster 1→2) renames the top-level `passives` field to `triggeredEffects`.

## The scan/apply tool (`migrate.ts`)

Entry point `runMigrateGameData(context, onApplied?)`, wired to the command `gamedataEditor.migrateGameData` ("GameData: Scan & Migrate", registered in `extension.ts`, declared in `package.json`). **It only runs when invoked from the Command Palette** — there's no activation event or auto-trigger on file open/save.

1. **Scan.** Walks a hardcoded `MIGRATION_TARGETS` list (Adventurers, Monsters, Techs, Items, MonsterActions, Jobs) — deliberately separate from `gameDataLoader.ts`'s `CATEGORY_DEFINITIONS`, so MonsterActions (which has no Form Editor UI) still gets scanned for drift.
2. **Per file:** parse the JSON; if `schemaVersion` is missing or behind the schema's target, run `runMigrations`; then re-validate the (possibly now-patched) data against the schema with `validateAgainstSchema`. Each file is classified:
   - `ok` — already valid, nothing to do.
   - `migrated` — a migration ran, and the result now validates.
   - `needs-manual-fix` — parse failure, validation still fails after migration, or `runMigrations` reported `incomplete: true` (a version gap with no registered step).
3. **Report.** All `migrated`/`needs-manual-fix` entries (file path + notes) are written to a `GameData Migration` VS Code output channel, which is shown to the user. A warning message summarizes how many files need manual attention, if any.
4. **Apply, with confirmation.** If any files were auto-migrated, a modal **Apply / Cancel** prompt appears before anything touches disk. Only on **Apply** are the changed files overwritten in place (`fs.writeFileSync`, pretty-printed JSON), and `onApplied?.()` fires — wired to refresh the Form Editor's tree view if it's currently open.

**No backup of the original files is taken before overwriting.** This is a real gap in the tool as it stands today, not an intentional design choice — rely on version control (or a manual copy) if you want to review or revert what the migration changed.

## Extending: adding a new migration step

1. Bump the schema's `schemaVersion.const` in `src/GameEngine/Schemas/` (the canonical copy) and run `npm run copy-schemas` in `GameDataEditor` to propagate it — see [JSON Schemas](schemas.md).
2. Write a new `MigrationStep` in `migrations.ts` that mutates `data` in place and sets `data.schemaVersion` to the new version number.
3. Register it in `MIGRATIONS`, keyed by the schema's file name and the version it migrates *from* (not to).

`dungeon.schema.json` currently has no entries in `MIGRATIONS` at all — its `schemaVersion` hasn't needed bumping yet. That's expected, not a bug; add entries for it only once a change to that schema actually requires one. `monster.schema.json` got its first entry (`1: renameMonsterPassivesToV2`) once the `passives` → `triggeredEffects` rename gave it something to migrate.
