import * as vscode from 'vscode';
import { showFormEditorPanel, refreshFormEditorPanelIfOpen } from './formEditorPanel';
import { runMigrateGameData } from './migrate';

export function activate(context: vscode.ExtensionContext) {
	const openFormEditor = vscode.commands.registerCommand('gamedataEditor.openFormEditor', () => {
		showFormEditorPanel(context);
	});

	const migrateGameData = vscode.commands.registerCommand('gamedataEditor.migrateGameData', () => {
		runMigrateGameData(context, refreshFormEditorPanelIfOpen);
	});

	context.subscriptions.push(openFormEditor, migrateGameData);
}

export function deactivate() {}
