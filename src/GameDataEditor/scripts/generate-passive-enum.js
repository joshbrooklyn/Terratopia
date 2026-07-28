// Generates schemas/monster.schema.json's passives.items.enum from
// src/CombatEngine/Passives/PassiveRegistry.cs so the schema (used by both
// VS Code's native JSON validation and the Form Editor) stays in sync with
// the actual set of registered passives. Run via `npm run generate-schemas`.

const fs = require('fs');
const path = require('path');

const passivesDir = path.join(__dirname, '..', '..', 'CombatEngine', 'Passives');
const registryPath = path.join(passivesDir, 'PassiveRegistry.cs');
const schemaPath = path.join(__dirname, '..', 'schemas', 'monster.schema.json');

function getRegisteredPassiveNames() {
	const registrySource = fs.readFileSync(registryPath, 'utf8');

	const arrayMatch = registrySource.match(/_passives\s*=\s*new Passive\[\]\s*\{([^}]*)\}/);
	if (!arrayMatch) {
		throw new Error(`Could not find "_passives = new Passive[] { ... }" registration array in ${registryPath}`);
	}

	const classNames = [...arrayMatch[1].matchAll(/new (\w+)\(\)/g)].map(m => m[1]);
	if (classNames.length === 0) {
		throw new Error(`Found the passive registration array in ${registryPath}, but it contains no "new X()" entries`);
	}

	return classNames.map(className => {
		const classFile = path.join(passivesDir, `${className}.cs`);
		let source;
		try {
			source = fs.readFileSync(classFile, 'utf8');
		} catch (err) {
			throw new Error(`Passive class "${className}" is registered in PassiveRegistry.cs but ${classFile} could not be read: ${err.message}`);
		}

		const match = source.match(/PassiveName\s*=\s*"([^"]+)"/) || source.match(/Name\s*=>\s*"([^"]+)"/);
		if (!match) {
			throw new Error(`Could not resolve a passive name (expected "PassiveName = \\"...\\"" or "Name => \\"...\\"") in ${classFile}`);
		}

		return match[1];
	});
}

function main() {
	const passiveNames = getRegisteredPassiveNames();

	const schema = JSON.parse(fs.readFileSync(schemaPath, 'utf8'));
	schema.properties.passives.items.enum = passiveNames;
	schema.properties.passives.description =
		'Names of passive classes (src/CombatEngine/Passives) applied to this monster, e.g. "LivingDead". ' +
		'The enum is generated from PassiveRegistry.cs by `npm run generate-schemas` - do not hand-edit it.';

	fs.writeFileSync(schemaPath, JSON.stringify(schema, null, 2) + '\n', 'utf8');
	console.log(`generate-passive-enum: wrote ${passiveNames.length} passive(s) to ${schemaPath}: ${passiveNames.join(', ')}`);
}

main();
