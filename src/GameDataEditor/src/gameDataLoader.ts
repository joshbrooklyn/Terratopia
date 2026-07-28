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
}

export interface JsonSchemaProperty {
	type?: 'string' | 'integer' | 'number' | 'boolean' | 'array' | 'object';
	enum?: string[];
	items?: JsonSchemaProperty;
	properties?: Record<string, JsonSchemaProperty>;
	required?: string[];
	minimum?: number;
	maximum?: number;
	default?: unknown;
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
}

const CATEGORY_DEFINITIONS: CategoryDefinition[] = [
	{ category: 'Adventurers', folderName: 'Adventurers', idField: 'adventurerId', nameField: 'name', schemaFile: 'adventurer.schema.json' },
	{ category: 'Monsters', folderName: 'Monsters', idField: 'monsterId', nameField: 'name', schemaFile: 'monster.schema.json' },
	{ category: 'Techs', folderName: 'Techs', idField: 'techId', nameField: 'name', schemaFile: 'tech.schema.json' },
];

export function getCategoryDefinition(category: string): CategoryDefinition | undefined {
	return CATEGORY_DEFINITIONS.find(definition => definition.category === category);
}

export function getCategoryFolder(gameDataRoot: string, category: string): string | undefined {
	const definition = getCategoryDefinition(category);
	return definition ? path.join(gameDataRoot, definition.folderName) : undefined;
}

const schemaCache = new Map<string, JsonSchemaObject>();

export function loadSchemaForCategory(extensionUri: vscode.Uri, category: string): JsonSchemaObject | undefined {
	const definition = getCategoryDefinition(category);
	if (!definition) {
		return undefined;
	}

	const cached = schemaCache.get(definition.schemaFile);
	if (cached) {
		return cached;
	}

	const schemaPath = path.join(extensionUri.fsPath, 'schemas', definition.schemaFile);
	try {
		const raw = fs.readFileSync(schemaPath, 'utf8');
		const parsed: JsonSchemaObject = JSON.parse(raw);
		schemaCache.set(definition.schemaFile, parsed);
		return parsed;
	} catch (err) {
		console.warn(`GameData Editor: failed to load schema ${schemaPath}`, err);
		return undefined;
	}
}

export function findGameDataRoot(): string | undefined {
	const workspaceFolders = vscode.workspace.workspaceFolders;
	if (!workspaceFolders || workspaceFolders.length === 0) {
		return undefined;
	}

	let dir: string | undefined = workspaceFolders[0].uri.fsPath;
	while (dir) {
		const candidate = path.join(dir, 'GameData');
		if (fs.existsSync(candidate) && fs.statSync(candidate).isDirectory()) {
			return candidate;
		}
		const parent = path.dirname(dir);
		dir = parent === dir ? undefined : parent;
	}

	return undefined;
}

function loadCategory(gameDataRoot: string, definition: CategoryDefinition): GameDataItem[] {
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
	}));
}

export function loadCategoryItems(gameDataRoot: string, category: string): GameDataItem[] {
	const definition = getCategoryDefinition(category);
	if (!definition) {
		return [];
	}
	return loadCategory(gameDataRoot, definition);
}
