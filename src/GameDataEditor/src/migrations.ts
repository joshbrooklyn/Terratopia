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

/** v3 → v4 (tech/monsteraction) and v4 → v5 (item): widens the calcType enum with FixedAmount. Existing data's shape is unaffected, so this just bumps the version number. */
function bumpToV4(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 4;
	return { notes: [] };
}

function bumpToV5(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 5;
	return { notes: [] };
}

/** v5 → v6 (item): widens the calcType enum with PercentOfMax. v5 → v6 (tech/monsteraction): adds the optional buffDebuffStat/buffDebuffIsPositive/buffDebuffRounds parameters. Existing data's shape is unaffected either way, so this just bumps the version number. */
function bumpToV6(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 6;
	return { notes: [] };
}

/** v6 → v7 (item): adds the optional buffDebuffStat/buffDebuffIsPositive/buffDebuffRounds parameters. Existing data's shape is unaffected, so this just bumps the version number. */
function bumpToV7(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 7;
	return { notes: [] };
}

interface BuffDebuffConversion {
	notes: string[];
	ok: boolean;
}

/**
 * Folds the old single parameters.buffDebuffStat/buffDebuffIsPositive/buffDebuffRounds triple into
 * one parameters.buffsDebuffs entry targeting SelectedTargets — the old implicit behaviour, since
 * the triple always landed on the action's own targets. No authored data uses the old fields, so
 * this is defensive. Absent buffDebuffStat is a no-op (ok: true, no notes). A buffDebuffStat
 * present without a well-formed isPositive/rounds pair is left untouched with ok: false, so the
 * caller doesn't stamp the version and runMigrations reports the file as needs-manual-fix instead
 * of silently dropping half of it — the same fail-loud pattern as directEffectsToCombatFunction.
 */
function convertBuffDebuffToArray(data: Record<string, unknown>): BuffDebuffConversion {
	const parameters = data.parameters as Record<string, unknown> | undefined;
	if (!parameters || parameters.buffDebuffStat === undefined) {
		return { notes: [], ok: true };
	}

	const stat = parameters.buffDebuffStat;
	const isPositive = parameters.buffDebuffIsPositive;
	const rounds = parameters.buffDebuffRounds;
	if (typeof isPositive !== 'boolean' || typeof rounds !== 'number') {
		return {
			notes: ['Cannot auto-migrate: parameters.buffDebuffStat is set but buffDebuffIsPositive/buffDebuffRounds is missing or malformed. Author parameters.buffsDebuffs by hand.'],
			ok: false,
		};
	}

	parameters.buffsDebuffs = [{ stat, type: isPositive ? 'Positive' : 'Negative', target: 'SelectedTargets', rounds }];
	delete parameters.buffDebuffStat;
	delete parameters.buffDebuffIsPositive;
	delete parameters.buffDebuffRounds;

	return {
		notes: [`Converted parameters.buffDebuffStat/buffDebuffIsPositive/buffDebuffRounds → parameters.buffsDebuffs: ${JSON.stringify(parameters.buffsDebuffs)}`],
		ok: true,
	};
}

/** v6 → v7 (tech/monsteraction): see convertBuffDebuffToArray. */
function buffDebuffToArrayV7(data: Record<string, unknown>): MigrationResult {
	const { notes, ok } = convertBuffDebuffToArray(data);
	if (!ok) {
		return { notes };
	}
	data.schemaVersion = 7;
	return { notes };
}

/** v7 → v8 (item): same conversion as buffDebuffToArrayV7, one version later since item runs a version ahead of tech/monsteraction. */
function buffDebuffToArrayV8(data: Record<string, unknown>): MigrationResult {
	const { notes, ok } = convertBuffDebuffToArray(data);
	if (!ok) {
		return { notes };
	}
	data.schemaVersion = 8;
	return { notes };
}

/**
 * Backfills the new required parameters.buffsDebuffs[].untilRemoved on every existing entry,
 * defaulting to false — the only meaning an entry authored before untilRemoved existed could have
 * had. Unlike a pure bumpToVN, this can mutate data, so it's its own step.
 */
function backfillUntilRemoved(data: Record<string, unknown>, nextVersion: number): MigrationResult {
	const parameters = data.parameters as Record<string, unknown> | undefined;
	const buffsDebuffs = parameters?.buffsDebuffs;
	const notes: string[] = [];
	if (Array.isArray(buffsDebuffs)) {
		for (const entry of buffsDebuffs) {
			if (entry && typeof entry === 'object' && (entry as Record<string, unknown>).untilRemoved === undefined) {
				(entry as Record<string, unknown>).untilRemoved = false;
				notes.push('Set untilRemoved: false on an existing buffsDebuffs entry.');
			}
		}
	}
	data.schemaVersion = nextVersion;
	return { notes };
}

/** v7 → v8 (tech/monsteraction): see backfillUntilRemoved. */
function backfillUntilRemovedToV8(data: Record<string, unknown>): MigrationResult {
	return backfillUntilRemoved(data, 8);
}

/** v8 → v9 (item): see backfillUntilRemoved. */
function backfillUntilRemovedToV9(data: Record<string, unknown>): MigrationResult {
	return backfillUntilRemoved(data, 9);
}

/** v8 → v9 (monsteraction): drops the never-wired numTargets field; numAttacks/allowMultipleAttackOnSameTarget are new and optional (schema default / omittable), so no backfill is needed. */
function dropNumTargetsV9(data: Record<string, unknown>): MigrationResult {
	const had = data.numTargets !== undefined;
	delete data.numTargets;
	data.schemaVersion = 9;
	return { notes: had ? ['Removed numTargets (was never used by the engine).'] : [] };
}

/** Drops the never-wired traits field, common to tech/item/monsteraction. */
function dropTraits(data: Record<string, unknown>, nextVersion: number): MigrationResult {
	const had = data.traits !== undefined;
	delete data.traits;
	data.schemaVersion = nextVersion;
	return { notes: had ? ['Removed traits (was never used by the engine).'] : [] };
}

/** v8 → v9 (tech): see dropTraits. */
function dropTraitsV9(data: Record<string, unknown>): MigrationResult {
	return dropTraits(data, 9);
}

/** v9 → v10 (item/monsteraction): see dropTraits. */
function dropTraitsV10(data: Record<string, unknown>): MigrationResult {
	return dropTraits(data, 10);
}

/** v1 → v2: backfills the new required `timedBuffPct`, the global magnitude for timed buffs/debuffs (CombatEntity.AddBuffDebuff). */
function addTimedBuffPct(data: Record<string, unknown>): MigrationResult {
	data.timedBuffPct = 0.35;
	data.schemaVersion = 2;
	return { notes: ['Set timedBuffPct to 0.35.'] };
}

/** v2 → v3: backfills the new required `regenDrainHpPct`/`regenDrainTpPct`, the global magnitudes for timed regen/drain (CombatEntity.ApplyRegensDrains). */
function addRegenDrainPct(data: Record<string, unknown>): MigrationResult {
	data.regenDrainHpPct = 0.10;
	data.regenDrainTpPct = 0.10;
	data.schemaVersion = 3;
	return { notes: ['Set regenDrainHpPct and regenDrainTpPct to 0.10.'] };
}

/** v9 → v10 (tech): adds the optional `regensDrains` array; nothing to backfill. */
function bumpToV10(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 10;
	return { notes: [] };
}

/** v10 → v11 (item/monsteraction): adds the optional `regensDrains` array; nothing to backfill. */
function bumpToV11(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 11;
	return { notes: [] };
}

/**
 * Backfills the new required parameters.buffsDebuffs[]/regensDrains[].cancelOnEntityDeath and
 * .cancelOnApplierDeath on every existing entry, defaulting to true/false respectively - matching
 * the GameData Editor's new-entry defaults and the only meaning an entry authored before these
 * flags existed could have had. Unlike a pure bumpToVN, this can mutate data, so it's its own step.
 */
function backfillCancelOnDeathFlags(data: Record<string, unknown>, nextVersion: number): MigrationResult {
	const parameters = data.parameters as Record<string, unknown> | undefined;
	const notes: string[] = [];

	for (const arrayName of ['buffsDebuffs', 'regensDrains'] as const) {
		const entries = parameters?.[arrayName];
		if (!Array.isArray(entries)) continue;

		for (const entry of entries) {
			if (!entry || typeof entry !== 'object') continue;
			const e = entry as Record<string, unknown>;
			if (e.cancelOnEntityDeath === undefined) {
				e.cancelOnEntityDeath = true;
				notes.push(`Set cancelOnEntityDeath: true on an existing ${arrayName} entry.`);
			}
			if (e.cancelOnApplierDeath === undefined) {
				e.cancelOnApplierDeath = false;
				notes.push(`Set cancelOnApplierDeath: false on an existing ${arrayName} entry.`);
			}
		}
	}

	data.schemaVersion = nextVersion;
	return { notes };
}

/** v10 → v11 (tech): see backfillCancelOnDeathFlags. */
function backfillCancelOnDeathFlagsToV11(data: Record<string, unknown>): MigrationResult {
	return backfillCancelOnDeathFlags(data, 11);
}

/** v11 → v12 (item/monsteraction): see backfillCancelOnDeathFlags. */
function backfillCancelOnDeathFlagsToV12(data: Record<string, unknown>): MigrationResult {
	return backfillCancelOnDeathFlags(data, 12);
}

/** v11 → v12 (tech): adds the new 'NoDirectEffect' combat function. */
function bumpToV12(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 12;
	return { notes: [] };
}

/** v12 → v13 (item/monsteraction): adds the new 'NoDirectEffect' combat function. */
function bumpToV13(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = 13;
	return { notes: [] };
}

/** v12 → v13 (tech) / v13 → v14 (item/monsteraction): adds the optional `passivesApplied` array; nothing to backfill. */
function addPassivesApplied(data: Record<string, unknown>): MigrationResult {
	data.schemaVersion = (typeof data.schemaVersion === 'number' ? data.schemaVersion : 0) + 1;
	return { notes: [] };
}

/**
 * Renames parameters.passivesApplied → triggeredEffectsApplied, and each entry's `passive` field
 * → `triggeredEffect`. Part of the "Passive" → "TriggeredEffect" rename across the whole system.
 */
function renamePassivesApplied(data: Record<string, unknown>, nextVersion: number): MigrationResult {
	const parameters = data.parameters as Record<string, unknown> | undefined;
	const notes: string[] = [];

	const entries = parameters?.passivesApplied;
	if (Array.isArray(entries)) {
		for (const entry of entries) {
			if (!entry || typeof entry !== 'object') continue;
			const e = entry as Record<string, unknown>;
			if ('passive' in e) {
				e.triggeredEffect = e.passive;
				delete e.passive;
			}
		}
		parameters!.triggeredEffectsApplied = entries;
		delete parameters!.passivesApplied;
		notes.push('Renamed passivesApplied to triggeredEffectsApplied.');
	}

	data.schemaVersion = nextVersion;
	return { notes };
}

/** v13 → v14 (tech): see renamePassivesApplied. */
function renamePassivesAppliedToV14(data: Record<string, unknown>): MigrationResult {
	return renamePassivesApplied(data, 14);
}

/** v14 → v15 (item): see renamePassivesApplied. */
function renamePassivesAppliedToV15(data: Record<string, unknown>): MigrationResult {
	return renamePassivesApplied(data, 15);
}

/** v15 → v16 (monsteraction): see renamePassivesApplied. */
function renamePassivesAppliedToV16(data: Record<string, unknown>): MigrationResult {
	return renamePassivesApplied(data, 16);
}

/** v1 → v2 (monster): renames `passives` → `triggeredEffects`; nothing else to backfill. */
function renameMonsterPassivesToV2(data: Record<string, unknown>): MigrationResult {
	const notes: string[] = [];
	if ('passives' in data) {
		data.triggeredEffects = data.passives;
		delete data.passives;
		notes.push('Renamed passives to triggeredEffects.');
	}
	data.schemaVersion = 2;
	return { notes };
}

/** v1 → v2 (adventurer): backfills the new required `jobId`. Defaults to "fighter" — reassign each adventurer's job by hand. */
function addJobId(data: Record<string, unknown>): MigrationResult {
	data.jobId = 'fighter';
	data.schemaVersion = 2;
	return { notes: ['Set jobId to "fighter" — reassign each adventurer\'s job by hand.'] };
}

/** v14 → v15 (monsteraction): drops the never-wired jobClass field; nothing reads it. */
function dropJobClassV15(data: Record<string, unknown>): MigrationResult {
	const had = data.jobClass !== undefined;
	delete data.jobClass;
	data.schemaVersion = 15;
	return { notes: had ? ['Removed jobClass (was never used by the engine).'] : [] };
}

/** v2 → v3 (adventurer): drops the primary stats, which moved to Job as hpBase/tpBase/powerBase/defenseBase/speedBase. */
function dropAdventurerBaseStatsV3(data: Record<string, unknown>): MigrationResult {
	const fields = ['maxHp', 'hp', 'maxTp', 'tp', 'power', 'defense', 'speed'] as const;
	const removed = fields.filter(f => data[f] !== undefined);
	for (const f of fields) delete data[f];
	data.schemaVersion = 3;
	return { notes: removed.length > 0 ? [`Removed ${removed.join(', ')} — base stats now live on the adventurer's Job.`] : [] };
}

/** v1 → v2 (job): backfills the new required base stats, moved here from Adventurer. Defaults to 80/50/10/10/10 — the block every existing adventurer carried — tune per job by hand. */
function addJobBaseStatsV2(data: Record<string, unknown>): MigrationResult {
	data.hpBase = 80;
	data.tpBase = 50;
	data.powerBase = 10;
	data.defenseBase = 10;
	data.speedBase = 10;
	data.schemaVersion = 2;
	return { notes: ['Set hpBase/tpBase/powerBase/defenseBase/speedBase to 80/50/10/10/10 — tune per job by hand.'] };
}

/** Migration steps, keyed by schema file name, then by the version being migrated *from*. */
const MIGRATIONS: Record<string, Record<number, MigrationStep>> = {
	'item.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: addMaxUses, 4: bumpToV5, 5: bumpToV6, 6: bumpToV7, 7: buffDebuffToArrayV8, 8: backfillUntilRemovedToV9, 9: dropTraitsV10, 10: bumpToV11, 11: backfillCancelOnDeathFlagsToV12, 12: bumpToV13, 13: addPassivesApplied, 14: renamePassivesAppliedToV15 },
	'tech.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4, 4: bumpToV5, 5: bumpToV6, 6: buffDebuffToArrayV7, 7: backfillUntilRemovedToV8, 8: dropTraitsV9, 9: bumpToV10, 10: backfillCancelOnDeathFlagsToV11, 11: bumpToV12, 12: addPassivesApplied, 13: renamePassivesAppliedToV14 },
	'monsteraction.schema.json': { 1: stripInvalidKeywords, 2: directEffectsToCombatFunction, 3: bumpToV4, 4: bumpToV5, 5: bumpToV6, 6: buffDebuffToArrayV7, 7: backfillUntilRemovedToV8, 8: dropNumTargetsV9, 9: dropTraitsV10, 10: bumpToV11, 11: backfillCancelOnDeathFlagsToV12, 12: bumpToV13, 13: addPassivesApplied, 14: dropJobClassV15, 15: renamePassivesAppliedToV16 },
	'monster.schema.json': { 1: renameMonsterPassivesToV2 },
	'gamesettings.schema.json': { 1: addTimedBuffPct, 2: addRegenDrainPct },
	'adventurer.schema.json': { 1: addJobId, 2: dropAdventurerBaseStatsV3 },
	'job.schema.json': { 1: addJobBaseStatsV2 },
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
