import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { findGameDataRoot, getSchemaVersion, loadSchemaByFileName } from './gameDataLoader';
import { validateAgainstSchema } from './schemaValidation';
import { runMigrations } from './migrations';

/**
 * On-disk folders to scan, independent of CATEGORY_DEFINITIONS in gameDataLoader.ts —
 * that list drives the tree UI and doesn't include MonsterActions, which has no
 * form-editor support yet but still needs its schemaVersion/enum drift caught here.
 */
const MIGRATION_TARGETS: { folderName: string; schemaFile: string }[] = [
	{ folderName: 'Adventurers', schemaFile: 'adventurer.schema.json' },
	{ folderName: 'Monsters', schemaFile: 'monster.schema.json' },
	{ folderName: 'Techs', schemaFile: 'tech.schema.json' },
	{ folderName: 'Items', schemaFile: 'item.schema.json' },
	{ folderName: 'MonsterActions', schemaFile: 'monsteraction.schema.json' },
];

interface ScanEntry {
	filePath: string;
	folderName: string;
	status: 'ok' | 'migrated' | 'needs-manual-fix';
	notes: string[];
	data?: Record<string, unknown>;
}

let outputChannel: vscode.OutputChannel | undefined;

function getOutputChannel(): vscode.OutputChannel {
	if (!outputChannel) {
		outputChannel = vscode.window.createOutputChannel('GameData Migration');
	}
	return outputChannel;
}

function scanFile(extensionUri: vscode.Uri, folderName: string, schemaFile: string, filePath: string): ScanEntry {
	const schema = loadSchemaByFileName(extensionUri, schemaFile);
	if (!schema) {
		return { filePath, folderName, status: 'needs-manual-fix', notes: [`No schema found for ${schemaFile}.`] };
	}

	let data: Record<string, unknown>;
	try {
		data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
	} catch (err) {
		return { filePath, folderName, status: 'needs-manual-fix', notes: [`Failed to parse: ${err}`] };
	}

	const notes: string[] = [];
	const currentVersion = typeof data.schemaVersion === 'number' ? data.schemaVersion : undefined;
	let changed = false;
	let incomplete = false;

	if (currentVersion === undefined || currentVersion < getSchemaVersion(schema)) {
		const before = JSON.stringify(data);
		const result = runMigrations(schemaFile, schema, data);
		notes.push(...result.notes);
		changed = JSON.stringify(data) !== before;
		incomplete = result.incomplete;
		if (incomplete) {
			notes.push('No automatic migration available for the remaining version gap — needs manual review.');
		}
	}

	const errors = validateAgainstSchema(schema, data);
	if (errors.length > 0) {
		notes.push(...errors.map(e => `${e.path}: ${e.message}`));
		return { filePath, folderName, status: 'needs-manual-fix', notes };
	}

	if (incomplete) {
		return { filePath, folderName, status: 'needs-manual-fix', notes };
	}

	if (changed) {
		return { filePath, folderName, status: 'migrated', notes, data };
	}

	return { filePath, folderName, status: 'ok', notes: [] };
}

function scanAll(extensionUri: vscode.Uri, gameDataRoot: string): ScanEntry[] {
	const entries: ScanEntry[] = [];
	for (const target of MIGRATION_TARGETS) {
		const dir = path.join(gameDataRoot, target.folderName);
		if (!fs.existsSync(dir)) {
			continue;
		}
		const files = fs.readdirSync(dir)
			.filter(f => f.toLowerCase().endsWith('.json'))
			.filter(f => path.basename(f, '.json').toLowerCase() !== 'notimplemented')
			.sort();

		for (const fileName of files) {
			entries.push(scanFile(extensionUri, target.folderName, target.schemaFile, path.join(dir, fileName)));
		}
	}
	return entries;
}

export async function runMigrateGameData(context: vscode.ExtensionContext, onApplied?: () => void): Promise<void> {
	const gameDataRoot = findGameDataRoot();
	if (!gameDataRoot) {
		vscode.window.showErrorMessage('GameData Editor: could not locate a GameData folder in this workspace.');
		return;
	}

	const entries = scanAll(context.extensionUri, gameDataRoot);
	const migrated = entries.filter(e => e.status === 'migrated');
	const needsManualFix = entries.filter(e => e.status === 'needs-manual-fix');

	const channel = getOutputChannel();
	channel.clear();
	channel.appendLine(`GameData Migration scan — ${entries.length} file(s) checked.`);
	for (const entry of [...migrated, ...needsManualFix]) {
		channel.appendLine(`\n[${entry.status}] ${entry.filePath}`);
		for (const note of entry.notes) {
			channel.appendLine(`  - ${note}`);
		}
	}
	channel.show(true);

	if (needsManualFix.length > 0) {
		vscode.window.showWarningMessage(`GameData Migration: ${needsManualFix.length} file(s) need manual fixes — see the "GameData Migration" output channel.`);
	}

	if (migrated.length === 0) {
		vscode.window.showInformationMessage('GameData Migration: no files need automatic migration.');
		return;
	}

	const choice = await vscode.window.showWarningMessage(
		`GameData Migration: ${migrated.length} file(s) can be auto-migrated. Apply changes?`,
		'Apply',
		'Cancel'
	);

	if (choice !== 'Apply') {
		return;
	}

	for (const entry of migrated) {
		if (!entry.data) {
			continue;
		}
		fs.writeFileSync(entry.filePath, JSON.stringify(entry.data, null, 2) + '\n', 'utf8');
	}

	vscode.window.showInformationMessage(`GameData Migration: applied changes to ${migrated.length} file(s).`);
	onApplied?.();
}
