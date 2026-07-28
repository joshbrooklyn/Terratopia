(function () {
	const vscode = acquireVsCodeApi();
	const treeRoot = document.getElementById('tree-root');
	const detailPane = document.getElementById('detail-pane');

	let selectedRow = null;
	let currentDetail = null; // { filePath, category, schema, name }
	let formState = {};
	let isDirty = false;
	let fieldElementsByPath = new Map();
	let lastValidationErrors = [];

	const FIELD_VISIBILITY_RULES = {
		Techs: {
			allowMultipleAttackOnSameTarget: {
				watch: ['targetingType'],
				visible: state => !['All', 'Self'].includes(state.targetingType),
			},
		},
	};

	const DISABLED_FIELDS = {
		Techs: ['keywords', 'traits', 'targetStatuses', 'userStatuses'],
		Adventurers: ['itemIds'],
		Monsters: ['monsterActionIds'],
	};

	function isFieldDisabled(category, key) {
		const fields = DISABLED_FIELDS[category];
		return !!fields && fields.includes(key);
	}

	function humanizeFieldName(key) {
		return key.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase());
	}

	function appendDisabledNote(row, key, disabled) {
		if (!disabled) {
			return;
		}
		const note = document.createElement('p');
		note.className = 'disabled-note';
		note.textContent = `${humanizeFieldName(key)} not implemented in game yet`;
		row.appendChild(note);
	}

	function getVisibilityRule(category, key) {
		const rules = FIELD_VISIBILITY_RULES[category];
		return rules ? rules[key] : undefined;
	}

	function isWatchedField(category, key) {
		const rules = FIELD_VISIBILITY_RULES[category];
		if (!rules) {
			return false;
		}
		return Object.values(rules).some(rule => rule.watch.includes(key));
	}

	function selectItem(row, item, category) {
		if (selectedRow) {
			selectedRow.classList.remove('selected');
		}
		row.classList.add('selected');
		selectedRow = row;
		vscode.postMessage({ type: 'select', filePath: item.filePath, category: category.category });
	}

	function withDirtyCheck(action) {
		if (isDirty) {
			showConfirmModal('Discard unsaved changes to the current item?').then(confirmed => {
				if (confirmed) {
					action();
				}
			});
		} else {
			action();
		}
	}

	function showConfirmModal(message) {
		return new Promise(resolve => {
			const overlay = document.createElement('div');
			overlay.className = 'modal-overlay';

			const dialog = document.createElement('div');
			dialog.className = 'modal-dialog';

			const text = document.createElement('p');
			text.textContent = message;
			dialog.appendChild(text);

			const actions = document.createElement('div');
			actions.className = 'modal-actions';

			const cancelBtn = document.createElement('button');
			cancelBtn.className = 'btn btn-secondary';
			cancelBtn.textContent = 'Cancel';
			cancelBtn.addEventListener('click', () => {
				overlay.remove();
				resolve(false);
			});
			actions.appendChild(cancelBtn);

			const discardBtn = document.createElement('button');
			discardBtn.className = 'btn btn-remove';
			discardBtn.textContent = 'Discard';
			discardBtn.addEventListener('click', () => {
				overlay.remove();
				resolve(true);
			});
			actions.appendChild(discardBtn);

			dialog.appendChild(actions);
			overlay.appendChild(dialog);
			document.body.appendChild(overlay);
		});
	}

	function renderTree(categories) {
		treeRoot.innerHTML = '';

		for (const category of categories) {
			const details = document.createElement('details');
			details.className = 'category';
			details.open = true;

			const summary = document.createElement('summary');

			const label = document.createElement('span');
			label.textContent = `${category.category} (${category.items.length})`;
			summary.appendChild(label);

			const newBtn = document.createElement('button');
			newBtn.className = 'btn btn-secondary btn-small';
			newBtn.textContent = '+ New';
			newBtn.addEventListener('click', event => {
				event.preventDefault();
				event.stopPropagation();
				withDirtyCheck(() => {
					vscode.postMessage({ type: 'new', category: category.category });
				});
			});
			summary.appendChild(newBtn);

			details.appendChild(summary);

			if (category.items.length === 0) {
				const empty = document.createElement('div');
				empty.className = 'empty-category';
				empty.textContent = 'No items';
				details.appendChild(empty);
			} else {
				const list = document.createElement('ul');
				list.className = 'item-list';

				for (const item of category.items) {
					const row = document.createElement('li');
					row.className = 'item-row';
					row.title = item.id;

					const nameSpan = document.createElement('span');
					nameSpan.className = 'item-name';
					nameSpan.textContent = item.name;
					nameSpan.addEventListener('click', () => {
						withDirtyCheck(() => selectItem(row, item, category));
					});
					row.appendChild(nameSpan);

					const copyBtn = document.createElement('button');
					copyBtn.className = 'btn btn-secondary btn-small copy-btn';
					copyBtn.textContent = 'Copy';
					copyBtn.addEventListener('click', event => {
						event.stopPropagation();
						withDirtyCheck(() => {
							vscode.postMessage({ type: 'copy', filePath: item.filePath, category: category.category });
						});
					});
					row.appendChild(copyBtn);

					list.appendChild(row);
				}

				details.appendChild(list);
			}

			treeRoot.appendChild(details);
		}
	}

	function initStateFromSchema(schemaLike, sourceData) {
		const src = sourceData || {};
		const state = {};
		for (const [key, propSchema] of Object.entries(schemaLike.properties || {})) {
			if (propSchema.type === 'array') {
				const arr = Array.isArray(src[key]) ? src[key] : [];
				if (propSchema.items && propSchema.items.type === 'object') {
					const itemSchemaLike = { properties: propSchema.items.properties || {}, required: propSchema.items.required || [] };
					state[key] = arr.map(entry => initStateFromSchema(itemSchemaLike, entry));
				} else {
					state[key] = arr.slice();
				}
			} else {
				state[key] = src[key] !== undefined ? src[key] : propSchema.default;
			}
		}
		return state;
	}

	function serializeObject(schemaLike, state, category) {
		const result = {};
		for (const [key, propSchema] of Object.entries(schemaLike.properties || {})) {
			const rule = getVisibilityRule(category, key);
			if (rule && !rule.visible(state)) {
				continue;
			}

			const value = state[key];
			if (propSchema.type === 'array') {
				const arr = Array.isArray(value) ? value : [];
				if (propSchema.items && propSchema.items.type === 'object') {
					const itemSchemaLike = { properties: propSchema.items.properties || {}, required: propSchema.items.required || [] };
					result[key] = arr.map(entry => serializeObject(itemSchemaLike, entry, undefined));
				} else {
					result[key] = arr.slice();
				}
			} else {
				if (value === undefined || value === '') {
					continue;
				}
				result[key] = value;
			}
		}
		return result;
	}

	function onFieldChanged(key, category) {
		isDirty = true;
		if (isWatchedField(category, key)) {
			renderForm();
		} else {
			updateValidationUI();
		}
	}

	function joinPathClient(prefix, key) {
		return prefix ? `${prefix}.${key}` : key;
	}

	function validateRangeClient(propSchema, value, path) {
		const errors = [];
		if (propSchema.minimum !== undefined && value < propSchema.minimum) {
			errors.push({ path, message: `Must be >= ${propSchema.minimum}` });
		}
		if (propSchema.maximum !== undefined && value > propSchema.maximum) {
			errors.push({ path, message: `Must be <= ${propSchema.maximum}` });
		}
		return errors;
	}

	function validatePropertyClient(propSchema, value, path) {
		switch (propSchema.type) {
			case 'string':
				if (typeof value !== 'string') {
					return [{ path, message: 'Expected a string' }];
				}
				if (propSchema.enum && !propSchema.enum.includes(value)) {
					return [{ path, message: `Expected one of: ${propSchema.enum.join(', ')}` }];
				}
				return [];
			case 'integer':
				if (typeof value !== 'number' || !Number.isInteger(value)) {
					return [{ path, message: 'Expected an integer' }];
				}
				return validateRangeClient(propSchema, value, path);
			case 'number':
				if (typeof value !== 'number' || !Number.isFinite(value)) {
					return [{ path, message: 'Expected a number' }];
				}
				return validateRangeClient(propSchema, value, path);
			case 'boolean':
				if (typeof value !== 'boolean') {
					return [{ path, message: 'Expected a boolean' }];
				}
				return [];
			case 'array': {
				if (!Array.isArray(value)) {
					return [{ path, message: 'Expected an array' }];
				}
				const itemSchema = propSchema.items;
				if (!itemSchema) {
					return [];
				}
				const errors = [];
				value.forEach((item, index) => {
					const itemPath = `${path}[${index}]`;
					if (itemSchema.type === 'object') {
						if (typeof item !== 'object' || item === null || Array.isArray(item)) {
							errors.push({ path: itemPath, message: 'Expected an object' });
						} else {
							errors.push(...validateObjectClient(
								{ properties: itemSchema.properties || {}, required: itemSchema.required },
								item,
								itemPath
							));
						}
					} else {
						errors.push(...validatePropertyClient(itemSchema, item, itemPath));
					}
				});
				return errors;
			}
			default:
				return [];
		}
	}

	function validateObjectClient(schemaLike, data, pathPrefix) {
		const errors = [];
		const properties = schemaLike.properties || {};

		for (const key of schemaLike.required || []) {
			if (!(key in data) || data[key] === undefined) {
				errors.push({ path: joinPathClient(pathPrefix, key), message: 'Required property is missing' });
			}
		}

		for (const [key, propSchema] of Object.entries(properties)) {
			if (data[key] === undefined) {
				continue;
			}
			errors.push(...validatePropertyClient(propSchema, data[key], joinPathClient(pathPrefix, key)));
		}

		return errors;
	}

	function validateAgainstSchemaClient(schema, data) {
		return validateObjectClient(schema, data, '');
	}

	function clearFieldMarkers() {
		for (const entry of fieldElementsByPath.values()) {
			entry.input.classList.remove('invalid');
			if (entry.errorMsgEl) {
				entry.errorMsgEl.textContent = '';
				entry.errorMsgEl.style.display = 'none';
			}
		}
	}

	function updateValidationUI() {
		clearFieldMarkers();

		if (!currentDetail) {
			lastValidationErrors = [];
			return;
		}

		const data = serializeObject(currentDetail.schema, formState, currentDetail.category);
		const errors = validateAgainstSchemaClient(currentDetail.schema, data);
		lastValidationErrors = errors;

		for (const err of errors) {
			const entry = fieldElementsByPath.get(err.path);
			if (!entry) {
				continue;
			}
			entry.input.classList.add('invalid');
			if (entry.errorMsgEl) {
				entry.errorMsgEl.textContent = err.message;
				entry.errorMsgEl.style.display = 'block';
			}
		}

		const errorsBox = document.getElementById('form-errors');
		if (errorsBox) {
			errorsBox.innerHTML = '';
			if (errors.length > 0) {
				errorsBox.classList.add('visible');
				const title = document.createElement('div');
				title.textContent = 'Fix the following before saving:';
				errorsBox.appendChild(title);
				const list = document.createElement('ul');
				for (const err of errors) {
					const li = document.createElement('li');
					li.textContent = `${err.path}: ${err.message}`;
					list.appendChild(li);
				}
				errorsBox.appendChild(list);
			} else {
				errorsBox.classList.remove('visible');
			}
		}

		const saveBtn = document.getElementById('save-btn');
		if (saveBtn) {
			saveBtn.disabled = errors.length > 0;
		}
	}

	function renderField(key, propSchema, required, state, category, fieldPath) {
		const row = document.createElement('div');
		row.className = 'form-row';
		const disabled = isFieldDisabled(category, key);
		if (disabled) {
			row.classList.add('disabled');
			row.title = `${humanizeFieldName(key)} not implemented in game yet`;
		}

		if (propSchema.type === 'array' && propSchema.items && propSchema.items.type === 'object') {
			return renderObjectListField(row, key, propSchema, required, state, disabled, fieldPath);
		}
		if (propSchema.type === 'array') {
			return renderStringListField(row, key, propSchema, required, state, disabled, fieldPath);
		}

		const label = document.createElement('label');
		label.textContent = required ? `${key} ` : key;
		if (required) {
			const marker = document.createElement('span');
			marker.className = 'required-marker';
			marker.textContent = '*';
			label.appendChild(marker);
		}
		row.appendChild(label);
		appendDisabledNote(row, key, disabled);

		let input;
		if (propSchema.enum) {
			input = document.createElement('select');
			if (!required) {
				const blank = document.createElement('option');
				blank.value = '';
				blank.textContent = '(none)';
				input.appendChild(blank);
			}
			for (const option of propSchema.enum) {
				const opt = document.createElement('option');
				opt.value = option;
				opt.textContent = option;
				input.appendChild(opt);
			}
			input.value = state[key] !== undefined ? state[key] : '';
			input.addEventListener('change', () => {
				state[key] = input.value === '' ? undefined : input.value;
				onFieldChanged(key, category);
			});
		} else if (propSchema.type === 'boolean') {
			input = document.createElement('input');
			input.type = 'checkbox';
			input.checked = state[key] === true;
			input.addEventListener('change', () => {
				state[key] = input.checked;
				onFieldChanged(key, category);
			});
		} else if (propSchema.type === 'integer' || propSchema.type === 'number') {
			input = document.createElement('input');
			input.type = 'number';
			input.step = propSchema.type === 'integer' ? '1' : 'any';
			if (propSchema.minimum !== undefined) {
				input.min = String(propSchema.minimum);
			}
			if (propSchema.maximum !== undefined) {
				input.max = String(propSchema.maximum);
			}
			input.value = state[key] !== undefined ? String(state[key]) : '';
			input.addEventListener('input', () => {
				if (input.value === '') {
					state[key] = undefined;
				} else {
					const parsed = propSchema.type === 'integer' ? parseInt(input.value, 10) : parseFloat(input.value);
					state[key] = Number.isNaN(parsed) ? undefined : parsed;
				}
				onFieldChanged(key, category);
			});
		} else {
			input = document.createElement('input');
			input.type = 'text';
			input.value = state[key] !== undefined ? state[key] : '';
			input.addEventListener('input', () => {
				state[key] = input.value === '' ? undefined : input.value;
				onFieldChanged(key, category);
			});
		}

		input.disabled = disabled;
		row.appendChild(input);

		const errorMsgEl = document.createElement('div');
		errorMsgEl.className = 'field-error-msg';
		errorMsgEl.style.display = 'none';
		row.appendChild(errorMsgEl);
		fieldElementsByPath.set(fieldPath, { input, errorMsgEl });

		return row;
	}

	function renderStringListField(row, key, propSchema, required, state, disabled, fieldPath) {
		row.classList.add('array-row');

		const label = document.createElement('label');
		label.textContent = key + (required ? ' *' : '');
		row.appendChild(label);
		appendDisabledNote(row, key, disabled);

		const values = Array.isArray(state[key]) ? state[key] : (state[key] = []);

		const list = document.createElement('ul');
		list.className = 'array-list';

		const itemEnum = propSchema.items && propSchema.items.enum;

		values.forEach((val, index) => {
			const li = document.createElement('li');
			const itemPath = `${fieldPath}[${index}]`;

			let input;
			if (itemEnum) {
				input = document.createElement('select');
				for (const option of itemEnum) {
					const opt = document.createElement('option');
					opt.value = option;
					opt.textContent = option;
					input.appendChild(opt);
				}
				input.value = val;
				input.addEventListener('change', () => {
					values[index] = input.value;
					isDirty = true;
					updateValidationUI();
				});
			} else {
				input = document.createElement('input');
				input.type = 'text';
				input.value = val;
				input.addEventListener('input', () => {
					values[index] = input.value;
					isDirty = true;
					updateValidationUI();
				});
			}
			input.disabled = !!disabled;
			li.appendChild(input);

			const removeBtn = document.createElement('button');
			removeBtn.className = 'btn btn-remove';
			removeBtn.textContent = '✕';
			removeBtn.title = 'Remove';
			removeBtn.disabled = !!disabled;
			removeBtn.addEventListener('click', () => {
				values.splice(index, 1);
				isDirty = true;
				renderForm();
			});
			li.appendChild(removeBtn);

			const errorMsgEl = document.createElement('div');
			errorMsgEl.className = 'field-error-msg';
			errorMsgEl.style.display = 'none';
			li.appendChild(errorMsgEl);
			fieldElementsByPath.set(itemPath, { input, errorMsgEl });

			list.appendChild(li);
		});

		row.appendChild(list);

		const addBtn = document.createElement('button');
		addBtn.className = 'btn btn-secondary';
		addBtn.textContent = 'Add';
		addBtn.disabled = !!disabled;
		addBtn.addEventListener('click', () => {
			values.push(itemEnum && itemEnum.length > 0 ? itemEnum[0] : '');
			isDirty = true;
			renderForm();
		});
		row.appendChild(addBtn);

		return row;
	}

	function renderObjectListField(row, key, propSchema, required, state, disabled, fieldPath) {
		row.classList.add('array-row');

		const label = document.createElement('label');
		label.textContent = key + (required ? ' *' : '');
		row.appendChild(label);
		appendDisabledNote(row, key, disabled);

		const entries = Array.isArray(state[key]) ? state[key] : (state[key] = []);
		const itemSchemaLike = { properties: propSchema.items.properties || {}, required: propSchema.items.required || [] };

		entries.forEach((entry, index) => {
			const fieldset = document.createElement('fieldset');
			fieldset.className = 'array-group';

			const header = document.createElement('div');
			header.className = 'array-group-header';

			const legend = document.createElement('span');
			legend.textContent = `${key} #${index + 1}`;
			header.appendChild(legend);

			const removeBtn = document.createElement('button');
			removeBtn.className = 'btn btn-remove';
			removeBtn.textContent = 'Remove';
			removeBtn.disabled = !!disabled;
			removeBtn.addEventListener('click', () => {
				entries.splice(index, 1);
				isDirty = true;
				renderForm();
			});
			header.appendChild(removeBtn);

			fieldset.appendChild(header);
			renderFieldsInto(fieldset, itemSchemaLike, entry, undefined, `${fieldPath}[${index}]`);
			row.appendChild(fieldset);
		});

		const addBtn = document.createElement('button');
		addBtn.className = 'btn btn-secondary';
		addBtn.textContent = 'Add entry';
		addBtn.disabled = !!disabled;
		addBtn.addEventListener('click', () => {
			entries.push(initStateFromSchema(itemSchemaLike, {}));
			isDirty = true;
			renderForm();
		});
		row.appendChild(addBtn);

		return row;
	}

	function renderFieldsInto(container, schemaLike, state, category, pathPrefix) {
		pathPrefix = pathPrefix || '';
		for (const [key, propSchema] of Object.entries(schemaLike.properties || {})) {
			const rule = getVisibilityRule(category, key);
			if (rule && !rule.visible(state)) {
				continue;
			}
			const required = (schemaLike.required || []).includes(key);
			const fieldPath = pathPrefix ? `${pathPrefix}.${key}` : key;
			container.appendChild(renderField(key, propSchema, required, state, category, fieldPath));
		}
	}

	function onSaveClicked() {
		if (!currentDetail) {
			return;
		}
		updateValidationUI();
		if (lastValidationErrors.length > 0) {
			return;
		}
		const data = serializeObject(currentDetail.schema, formState, currentDetail.category);
		vscode.postMessage({ type: 'save', filePath: currentDetail.filePath, category: currentDetail.category, data, isNew: !!currentDetail.isNew });
	}

	function renderForm() {
		detailPane.innerHTML = '';
		fieldElementsByPath = new Map();

		const heading = document.createElement('h2');
		heading.textContent = currentDetail.name;
		detailPane.appendChild(heading);

		const errorsBox = document.createElement('div');
		errorsBox.id = 'form-errors';
		detailPane.appendChild(errorsBox);

		const fields = document.createElement('div');
		fields.id = 'form-fields';
		renderFieldsInto(fields, currentDetail.schema, formState, currentDetail.category);
		detailPane.appendChild(fields);

		const actions = document.createElement('div');
		actions.id = 'form-actions';

		const saveBtn = document.createElement('button');
		saveBtn.id = 'save-btn';
		saveBtn.className = 'btn';
		saveBtn.textContent = 'Save';
		saveBtn.addEventListener('click', onSaveClicked);
		actions.appendChild(saveBtn);

		const status = document.createElement('span');
		status.id = 'save-status';
		actions.appendChild(status);

		detailPane.appendChild(actions);

		updateValidationUI();
	}

	function renderDetail(message) {
		detailPane.innerHTML = '';

		const heading = document.createElement('h2');
		heading.textContent = message.name;
		detailPane.appendChild(heading);

		if (message.error) {
			const err = document.createElement('p');
			err.className = 'detail-note';
			err.textContent = message.error;
			detailPane.appendChild(err);
			currentDetail = null;
			isDirty = false;
			return;
		}

		currentDetail = { filePath: message.filePath, category: message.category, schema: message.schema, name: message.name, isNew: !!message.isNew };
		formState = initStateFromSchema(message.schema, message.data);
		isDirty = false;
		renderForm();
	}

	function handleSaved(message) {
		isDirty = false;
		if (currentDetail) {
			currentDetail.filePath = message.filePath;
			currentDetail.isNew = false;
		}
		const status = document.getElementById('save-status');
		if (status) {
			status.textContent = 'Saved';
			status.className = 'save-status success';
		}
		const errorsBox = document.getElementById('form-errors');
		if (errorsBox) {
			errorsBox.innerHTML = '';
			errorsBox.classList.remove('visible');
		}
	}

	function handleSaveError(message) {
		const errorsBox = document.getElementById('form-errors');
		if (!errorsBox) {
			return;
		}
		errorsBox.innerHTML = '';
		errorsBox.classList.add('visible');

		const title = document.createElement('div');
		title.textContent = 'Could not save:';
		errorsBox.appendChild(title);

		const list = document.createElement('ul');
		for (const err of message.errors) {
			const li = document.createElement('li');
			li.textContent = err;
			list.appendChild(li);
		}
		errorsBox.appendChild(list);
	}

	window.addEventListener('message', event => {
		const message = event.data;
		switch (message.type) {
			case 'init':
				renderTree(message.categories);
				break;
			case 'detail':
				renderDetail(message);
				break;
			case 'saved':
				handleSaved(message);
				break;
			case 'save-error':
				handleSaveError(message);
				break;
		}
	});
}());
