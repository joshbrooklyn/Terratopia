import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import { findGameDataRoot, getCategoryDefinition, getCategoryFolder, loadAllCategories, loadCategoryItems, loadSchemaForCategory, JsonSchemaObject } from './gameDataLoader';
import { validateAgainstSchema } from './schemaValidation';

type HostToWebviewMessage =
	| { type: 'init'; categories: ReturnType<typeof loadAllCategories> }
	| { type: 'detail'; filePath: string | null; category: string; name: string; schema: ReturnType<typeof loadSchemaForCategory>; data: Record<string, unknown>; isNew: boolean }
	| { type: 'detail'; filePath: string | null; category: string; name: string; error: string }
	| { type: 'saved'; filePath: string }
	| { type: 'save-error'; filePath: string | null; errors: string[] };

type WebviewToHostMessage =
	| { type: 'select'; filePath: string; category: string }
	| { type: 'new'; category: string }
	| { type: 'copy'; filePath: string; category: string }
	| { type: 'save'; filePath: string | null; category: string; data: Record<string, unknown>; isNew: boolean };

let currentPanel: vscode.WebviewPanel | undefined;
let currentExtensionUri: vscode.Uri | undefined;
let currentGameDataRoot: string | undefined;

export function showFormEditorPanel(context: vscode.ExtensionContext): void {
	const gameDataRoot = findGameDataRoot();
	if (!gameDataRoot) {
		vscode.window.showErrorMessage('GameData Editor: could not locate a GameData folder in this workspace.');
		return;
	}

	currentExtensionUri = context.extensionUri;
	currentGameDataRoot = gameDataRoot;

	if (currentPanel) {
		currentPanel.reveal(vscode.ViewColumn.One);
	} else {
		currentPanel = vscode.window.createWebviewPanel(
			'gamedataEditor.formEditor',
			'GameData Form Editor',
			vscode.ViewColumn.One,
			{
				enableScripts: true,
				localResourceRoots: [vscode.Uri.joinPath(context.extensionUri, 'media')],
			}
		);

		currentPanel.onDidDispose(() => {
			currentPanel = undefined;
		}, null, context.subscriptions);

		currentPanel.webview.onDidReceiveMessage((message: WebviewToHostMessage) => {
			if (message?.type === 'select' && typeof message.filePath === 'string') {
				handleSelect(message.filePath, message.category);
			} else if (message?.type === 'new') {
				handleNew(message.category);
			} else if (message?.type === 'copy' && typeof message.filePath === 'string') {
				handleCopy(message.filePath, message.category);
			} else if (message?.type === 'save') {
				handleSave(message.filePath, message.category, message.data, message.isNew);
			}
		}, null, context.subscriptions);

		currentPanel.webview.html = getWebviewHtml(currentPanel.webview, context.extensionUri);
	}

	postToWebview({ type: 'init', categories: loadAllCategories(gameDataRoot) });
}

function postToWebview(message: HostToWebviewMessage): void {
	currentPanel?.webview.postMessage(message);
}

function getSchemaWithDynamicEnums(extensionUri: vscode.Uri, gameDataRoot: string | undefined, category: string): JsonSchemaObject | undefined {
	const schema = loadSchemaForCategory(extensionUri, category);
	if (!schema || category !== 'Adventurers' || !schema.properties.techsIds || !gameDataRoot) {
		return schema;
	}

	const techIds = loadCategoryItems(gameDataRoot, 'Techs').map(item => item.id).sort();
	return {
		...schema,
		properties: {
			...schema.properties,
			techsIds: {
				...schema.properties.techsIds,
				items: { ...schema.properties.techsIds.items, enum: techIds },
			},
		},
	};
}

function handleSelect(filePath: string, category: string): void {
	if (!currentExtensionUri) {
		return;
	}

	try {
		const raw = fs.readFileSync(filePath, 'utf8');
		const parsed = JSON.parse(raw);
		const schema = getSchemaWithDynamicEnums(currentExtensionUri, currentGameDataRoot, category);
		if (!schema) {
			postToWebview({ type: 'detail', filePath, category, name: parsed.name ?? filePath, error: `No schema found for category "${category}".` });
			return;
		}
		postToWebview({ type: 'detail', filePath, category, name: parsed.name ?? filePath, schema, data: parsed, isNew: false });
	} catch (err) {
		postToWebview({ type: 'detail', filePath, category, name: filePath, error: `Failed to read file: ${err}` });
	}
}

function handleNew(category: string): void {
	if (!currentExtensionUri) {
		return;
	}

	const schema = getSchemaWithDynamicEnums(currentExtensionUri, currentGameDataRoot, category);
	if (!schema) {
		postToWebview({ type: 'detail', filePath: null, category, name: '', error: `No schema found for category "${category}".` });
		return;
	}
	postToWebview({ type: 'detail', filePath: null, category, name: '', schema, data: {}, isNew: true });
}

function generateUniqueCopyId(baseId: string, existingIds: string[]): string {
	const existing = new Set(existingIds);
	let candidate = `${baseId}_copy`;
	let suffix = 2;
	while (existing.has(candidate)) {
		candidate = `${baseId}_copy${suffix}`;
		suffix++;
	}
	return candidate;
}

function handleCopy(filePath: string, category: string): void {
	if (!currentExtensionUri) {
		return;
	}

	const definition = getCategoryDefinition(category);
	if (!definition) {
		postToWebview({ type: 'detail', filePath: null, category, name: '', error: `No schema found for category "${category}".` });
		return;
	}

	try {
		const raw = fs.readFileSync(filePath, 'utf8');
		const parsed = JSON.parse(raw);
		const schema = getSchemaWithDynamicEnums(currentExtensionUri, currentGameDataRoot, category);
		if (!schema) {
			postToWebview({ type: 'detail', filePath: null, category, name: '', error: `No schema found for category "${category}".` });
			return;
		}

		const existingIds = currentGameDataRoot ? loadCategoryItems(currentGameDataRoot, category).map(item => item.id) : [];
		const data: Record<string, unknown> = { ...parsed };
		data[definition.nameField] = `${parsed[definition.nameField]} (Copy)`;
		data[definition.idField] = generateUniqueCopyId(String(parsed[definition.idField]), existingIds);

		postToWebview({ type: 'detail', filePath: null, category, name: String(data[definition.nameField]), schema, data, isNew: true });
	} catch (err) {
		postToWebview({ type: 'detail', filePath: null, category, name: filePath, error: `Failed to read file: ${err}` });
	}
}

function sanitizeFileNameSegment(name: string): string {
	return name
		.replace(/[<>:"/\\|?*\x00-\x1F]/g, '')
		.trim()
		.replace(/[. ]+$/, '');
}

function finishSave(filePath: string): void {
	postToWebview({ type: 'saved', filePath });
	vscode.window.showInformationMessage(`GameData Editor: saved ${filePath}`);

	if (currentGameDataRoot) {
		postToWebview({ type: 'init', categories: loadAllCategories(currentGameDataRoot) });
	}
}

function handleSave(filePath: string | null, category: string, data: Record<string, unknown>, isNew: boolean): void {
	if (!currentExtensionUri) {
		return;
	}

	const schema = getSchemaWithDynamicEnums(currentExtensionUri, currentGameDataRoot, category);
	if (!schema) {
		postToWebview({ type: 'save-error', filePath, errors: [`No schema found for category "${category}".`] });
		return;
	}

	const errors = validateAgainstSchema(schema, data);
	if (errors.length > 0) {
		postToWebview({ type: 'save-error', filePath, errors: errors.map(e => `${e.path}: ${e.message}`) });
		return;
	}

	let targetPath = filePath;

	if (isNew) {
		const definition = getCategoryDefinition(category);
		const folder = currentGameDataRoot ? getCategoryFolder(currentGameDataRoot, category) : undefined;
		if (!definition || !folder) {
			postToWebview({ type: 'save-error', filePath, errors: [`No schema found for category "${category}".`] });
			return;
		}

		const sanitizedName = sanitizeFileNameSegment(String(data[definition.nameField] ?? ''));
		if (!sanitizedName) {
			postToWebview({ type: 'save-error', filePath, errors: ['Name produces an invalid file name — please use different characters.'] });
			return;
		}

		targetPath = path.join(folder, `${sanitizedName}.json`);
		if (fs.existsSync(targetPath)) {
			postToWebview({ type: 'save-error', filePath, errors: [`A file named "${sanitizedName}.json" already exists in ${definition.folderName} — choose a different name.`] });
			return;
		}

		const existingIds = currentGameDataRoot ? loadCategoryItems(currentGameDataRoot, category).map(item => item.id) : [];
		if (existingIds.includes(String(data[definition.idField]))) {
			postToWebview({ type: 'save-error', filePath, errors: [`An item with id "${data[definition.idField]}" already exists.`] });
			return;
		}
	}

	if (!targetPath) {
		postToWebview({ type: 'save-error', filePath, errors: ['No file path to save to.'] });
		return;
	}

	try {
		fs.writeFileSync(targetPath, JSON.stringify(data, null, 2) + '\n', 'utf8');
	} catch (err) {
		postToWebview({ type: 'save-error', filePath, errors: [`Failed to write file: ${err}`] });
		return;
	}

	finishSave(targetPath);
}

function getNonce(): string {
	let text = '';
	const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
	for (let i = 0; i < 32; i++) {
		text += possible.charAt(Math.floor(Math.random() * possible.length));
	}
	return text;
}

function getWebviewHtml(webview: vscode.Webview, extensionUri: vscode.Uri): string {
	const scriptUri = webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, 'media', 'main.js'));
	const styleUri = webview.asWebviewUri(vscode.Uri.joinPath(extensionUri, 'media', 'main.css'));
	const nonce = getNonce();

	return /* html */ `<!DOCTYPE html>
<html lang="en">
<head>
	<meta charset="UTF-8">
	<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
	<link href="${styleUri}" rel="stylesheet">
	<title>GameData Form Editor</title>
</head>
<body>
	<div id="tree-pane">
		<div id="tree-root"></div>
	</div>
	<div id="detail-pane">
		<p class="placeholder">Select an item from the tree to view it.</p>
	</div>
	<script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
}
