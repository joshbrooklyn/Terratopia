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

/** Migration steps, keyed by schema file name, then by the version being migrated *from*. */
const MIGRATIONS: Record<string, Record<number, MigrationStep>> = {
	'item.schema.json': { 1: stripInvalidKeywords },
	'tech.schema.json': { 1: stripInvalidKeywords },
	'monsteraction.schema.json': { 1: stripInvalidKeywords },
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
