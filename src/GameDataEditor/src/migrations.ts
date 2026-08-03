import { JsonSchemaObject, getSchemaVersion } from './gameDataLoader';

export interface MigrationResult {
	notes: string[];
}

export type MigrationStep = (data: Record<string, unknown>, schema: JsonSchemaObject) => MigrationResult;

/** Strips any `keywords` entries that aren't in the schema's current enum, e.g. left over from before enum enforcement existed. */
function stripInvalidKeywords(data: Record<string, unknown>, schema: JsonSchemaObject): MigrationResult {
	const keywordEnum = schema.properties.keywords?.items?.enum;
	const keywords = data.keywords;
	if (!keywordEnum || !Array.isArray(keywords)) {
		data.schemaVersion = 2;
		return { notes: [] };
	}

	const removed = keywords.filter(k => !keywordEnum.includes(k));
	if (removed.length > 0) {
		data.keywords = keywords.filter(k => keywordEnum.includes(k));
	}
	data.schemaVersion = 2;

	return { notes: removed.map(k => `Removed invalid keyword "${k}"`) };
}

const BASIC_DAMAGE = 'BasicDamage';

/**
 * v2 → v3: replaces the `directEffects` array with a single `combatFunction` name plus a flat
 * `parameters` object, and drops the never-implemented status fields.
 *
 * Only the shape the engine actually supported converts cleanly: exactly one Damage effect.
 * Anything else — multiple effects, a Heal effect, no effects at all — is deliberately left
 * untouched and un-bumped, so runMigrations reports `incomplete` and migrate.ts flags the file as
 * needs-manual-fix rather than mangling it. There is no such data today; this is here so that if
 * any appears it fails loudly.
 */
function directEffectsToCombatFunction(data: Record<string, unknown>): MigrationResult {
	const effects = data.directEffects;

	if (!Array.isArray(effects) || effects.length !== 1) {
		const found = Array.isArray(effects) ? `${effects.length} entries` : 'no directEffects array';
		return { notes: [`Cannot auto-migrate: expected exactly one directEffects entry, found ${found}. Author combatFunction + parameters by hand.`] };
	}

	const effect = effects[0] as Record<string, unknown>;
	if (effect.effectType !== 'Damage') {
		return { notes: [`Cannot auto-migrate: directEffects[0].effectType is "${effect.effectType}" — map it to the right CombatFunction (e.g. BasicHeal) by hand.`] };
	}

	const parameters: Record<string, unknown> = {};
	for (const key of ['element', 'calcType', 'powerFactor']) {
		if (effect[key] !== undefined) {
			parameters[key] = effect[key];
		}
	}

	delete data.directEffects;
	data.combatFunction = BASIC_DAMAGE;
	data.parameters = parameters;

	const notes = [`Converted directEffects → combatFunction "${BASIC_DAMAGE}" with parameters ${JSON.stringify(parameters)}`];

	// targetStatuses/userStatuses were never read by the engine and are dropped in v3; their future
	// home is a CombatFunction parameter, not a top-level field.
	for (const dead of ['targetStatuses', 'userStatuses']) {
		const value = data[dead];
		if (value === undefined) {
			continue;
		}
		if (Array.isArray(value) && value.length > 0) {
			notes.push(`Dropped non-empty "${dead}": ${JSON.stringify(value)} — statuses are not implemented; re-author under parameters when they are.`);
		}
		delete data[dead];
	}

	data.schemaVersion = 3;
	return { notes };
}

/**
 * v3 → v4: backfills the new required `maxUses`. Defaults to 1, matching Obsidian/keywords.md's
 * "(almost) all Items have the single use keyword" — edit any item that should have more by hand.
 */
function addMaxUses(data: Record<string, unknown>): MigrationResult {
	data.maxUses = 1;
	data.schemaVersion = 4;
	return { notes: ['Set maxUses to 1 — edit any item that should have more.'] };
}

/** v3 → v4 (tech/monsteraction) and v4 → v5 (item): widens the calcType enum with FixedDamage. Existing data's shape is unaffected, so this just bumps the version number. */
function bumpToV4(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 4;
	return { notes: [] };
}

function bumpToV5(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 5;
	return { notes: [] };
}

/** Migration steps, keyed by schema file name, then by the version being migrated *from*. */
const MIGRATIONS: Record<string, Record<number, MigrationStep>> = {
	'item.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: addMaxUses, 4: bumpToV5 },
	'tech.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4 },
	'monsteraction.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4 },
};

export interface RunMigrationsResult {
	notes: string[];
	/** True if a needed version had no registered step — remaining versions were left unmigrated. */
	incomplete: boolean;
}

export function runMigrations(schemaFileName: string, schema: JsonSchemaObject, data: Record<string, unknown>): RunMigrationsResult {
	const targetVersion = getSchemaVersion(schema);
	const notes: string[] = [];

	let currentVersion = typeof data.schemaVersion === 'number' ? data.schemaVersion : 0;
	const stepsForSchema = MIGRATIONS[schemaFileName] ?? {};

	while (currentVersion < targetVersion) {
		const step = stepsForSchema[currentVersion];
		if (!step) {
			return { notes, incomplete: true };
		}
		const result = step(data, schema);
		notes.push(...result.notes);

		const nextVersion = typeof data.schemaVersion === 'number' ? data.schemaVersion : currentVersion;
		if (nextVersion <= currentVersion) {
			return { notes, incomplete: true };
		}
		currentVersion = nextVersion;
	}

	return { notes, incomplete: false };
}
