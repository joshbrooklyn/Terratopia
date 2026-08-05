import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';

export interface GameDataItem {
	id: string;
	name: string;
	fileName: string;
	filePath: string;
}

export interface GameDataCategory {
	category: string;
	folderName: string;
	items: GameDataItem[];
	// True for a category backed by one fixed file (e.g. GameSettings.json) rather than a folder
	// of many id/name-bearing entities. The tree view hides "New"/"Copy" for these.
	isSingleton?: boolean;
}

export interface JsonSchemaProperty {
	type?: 'string' | 'integer' | 'number' | 'boolean' | 'array' | 'object';
	enum?: string[];
	items?: JsonSchemaProperty;
	properties?: Record<string, JsonSchemaProperty>;
	required?: string[];
	minimum?: number;
	maximum?: number;
	uniqueItems?: boolean;
	// Array-of-object items only: entries are duplicates (rejected) when they agree on every
	// listed property, e.g. ["stat", "target"] for parameters.buffsDebuffs. A custom keyword —
	// draft-07 can't express "unique by property" — so it's inert to the C# Json.Schema
	// validator; the equivalent runtime check lives in CombatFunction.ApplyBuffsDebuffs.
	uniqueBy?: string[];
	default?: unknown;
	const?: unknown;
	description?: string;
}

export interface JsonSchemaObject {
	title?: string;
	type: 'object';
	additionalProperties: boolean;
	required: string[];
	properties: Record<string, JsonSchemaProperty>;
}

interface CategoryDefinition {
	category: string;
	folderName: string;
	idField: string;
	nameField: string;
	schemaFile: string;
	// When set, this category is one fixed file at `<gameDataRoot>/<singleFile>` rather than a
	// folder of many entity files - idField/nameField/folderName don't apply.
	singleFile?: string;
}

const CATEGORY_DEFINITIONS: CategoryDefinition[] = [
	{ category: 'Adventurers', folderName: 'Adventurers', idField: 'adventurerId', nameField: 'name', schemaFile: 'adventurer.schema.json' },
	{ category: 'Monsters', folderName: 'Monsters', idField: 'monsterId', nameField: 'name', schemaFile: 'monster.schema.json' },
	{ category: 'Techs', folderName: 'Techs', idField: 'techId', nameField: 'name', schemaFile: 'tech.schema.json' },
	{ category: 'Items', folderName: 'Items', idField: 'itemId', nameField: 'name', schemaFile: 'item.schema.json' },
	{ category: 'GameSettings', folderName: '', idField: '', nameField: '', schemaFile: 'gamesettings.schema.json', singleFile: 'GameSettings.json' },
];

/** Reads the authoritative schemaVersion (encoded as a `const` on the schema) so callers never hardcode it. */
export function getSchemaVersion(schema: JsonSchemaObject): number {
	const version = schema.properties.schemaVersion?.const;
	if (typeof version !== 'number') {
		throw new Error('Schema is missing a numeric schemaVersion.const.');
	}
	return version;
}

export function getCategoryDefinition(category: string): CategoryDefinition | undefined {
	return CATEGORY_DEFINITIONS.find(definition => definition.category === category);
}

export function getCategoryFolder(gameDataRoot: string, category: string): string | undefined {
	const definition = getCategoryDefinition(category);
	return definition ? path.join(gameDataRoot, definition.folderName) : undefined;
}

const schemaCache = new Map<string, JsonSchemaObject>();

export function loadSchemaByFileName(extensionUri: vscode.Uri, schemaFile: string): JsonSchemaObject | undefined {
	const cached = schemaCache.get(schemaFile);
	if (cached) {
		return cached;
	}

	const schemaPath = path.join(extensionUri.fsPath, 'schemas', schemaFile);
	try {
		const raw = fs.readFileSync(schemaPath, 'utf8');
		const parsed: JsonSchemaObject = JSON.parse(raw);
		schemaCache.set(schemaFile, parsed);
		return parsed;
	} catch (err) {
		console.warn(`GameData Editor: failed to load schema ${schemaPath}`, err);
		return undefined;
	}
}

export function loadSchemaForCategory(extensionUri: vscode.Uri, category: string): JsonSchemaObject | undefined {
	const definition = getCategoryDefinition(category);
	if (!definition) {
		return undefined;
	}

	return loadSchemaByFileName(extensionUri, definition.schemaFile);
}

export function findGameDataRoot(): string | undefined {
	const envPath = process.env.TerratopiaGameDataPath;
	if (!envPath) {
		return undefined;
	}
	if (fs.existsSync(envPath) && fs.statSync(envPath).isDirectory()) {
		return envPath;
	}
	return undefined;
}

function loadCategory(gameDataRoot: string, definition: CategoryDefinition): GameDataItem[] {
	if (definition.singleFile) {
		const filePath = path.join(gameDataRoot, definition.singleFile);
		if (!fs.existsSync(filePath)) {
			return [];
		}
		return [{ id: definition.category, name: definition.category, fileName: definition.singleFile, filePath }];
	}

	const dir = path.join(gameDataRoot, definition.folderName);
	if (!fs.existsSync(dir)) {
		return [];
	}

	const files = fs.readdirSync(dir)
		.filter(f => f.toLowerCase().endsWith('.json'))
		.filter(f => path.basename(f, '.json').toLowerCase() !== 'notimplemented')
		.sort();

	const items: GameDataItem[] = [];
	for (const fileName of files) {
		const filePath = path.join(dir, fileName);
		try {
			const raw = fs.readFileSync(filePath, 'utf8');
			const parsed = JSON.parse(raw);
			items.push({
				id: parsed[definition.idField] ?? fileName,
				name: parsed[definition.nameField] ?? fileName,
				fileName,
				filePath,
			});
		} catch (err) {
			console.warn(`GameData Editor: failed to parse ${filePath}`, err);
		}
	}

	return items;
}

export function loadAllCategories(gameDataRoot: string): GameDataCategory[] {
	return CATEGORY_DEFINITIONS.map(definition => ({
		category: definition.category,
		folderName: definition.folderName,
		items: loadCategory(gameDataRoot, definition),
		isSingleton: !!definition.singleFile,
	}));
}

export function loadCategoryItems(gameDataRoot: string, category: string): GameDataItem[] {
	const definition = getCategoryDefinition(category);
	if (!definition) {
		return [];
	}
	return loadCategory(gameDataRoot, definition);
}
