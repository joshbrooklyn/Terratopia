// Copies the canonical GameData schemas from src/GameEngine/Schemas into
// this extension's schemas/ folder, which is what VS Code's jsonValidation
// contribution and the Form Editor actually read from. GameEngine owns the
// schemas; this extension only ever sees generated copies. Run via
// `npm run copy-schemas` (wired into compile/watch, before generate-schemas).

const fs = require('fs');
const path = require('path');

const sourceDir = path.join(__dirname, '..', '..', 'GameEngine', 'Schemas');
const destDir = path.join(__dirname, '..', 'schemas');

function main() {
	fs.mkdirSync(destDir, { recursive: true });

	const schemaFiles = fs.readdirSync(sourceDir).filter(f => f.endsWith('.schema.json'));
	if (schemaFiles.length === 0) {
		throw new Error(`No *.schema.json files found in ${sourceDir}`);
	}

	for (const file of schemaFiles) {
		fs.copyFileSync(path.join(sourceDir, file), path.join(destDir, file));
	}

	console.log(`copy-schemas: copied ${schemaFiles.length} schema(s) from ${sourceDir} to ${destDir}: ${schemaFiles.join(', ')}`);
}

main();
