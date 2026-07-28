import * as vscode from 'vscode';

export function activate(context: vscode.ExtensionContext) {
	const openFormEditor = vscode.commands.registerCommand('gamedataEditor.openFormEditor', () => {
		vscode.window.showInformationMessage('Form editor coming soon — for now, edit GameData JSON directly (schema validation is active).');
	});

	context.subscriptions.push(openFormEditor);
}

export function deactivate() {}
