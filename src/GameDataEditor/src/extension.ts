import * as vscode from 'vscode';
import { showFormEditorPanel } from './formEditorPanel';

export function activate(context: vscode.ExtensionContext) {
	const openFormEditor = vscode.commands.registerCommand('gamedataEditor.openFormEditor', () => {
		showFormEditorPanel(context);
	});

	context.subscriptions.push(openFormEditor);
}

export function deactivate() {}
