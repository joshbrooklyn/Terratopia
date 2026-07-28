import { JsonSchemaObject, JsonSchemaProperty } from './gameDataLoader';

export interface ValidationError {
	path: string;
	message: string;
}

export function validateAgainstSchema(schema: JsonSchemaObject, data: Record<string, unknown>): ValidationError[] {
	return validateObject(schema, data, '');
}

function validateObject(schemaLike: { properties: Record<string, JsonSchemaProperty>; required?: string[] }, data: Record<string, unknown>, pathPrefix: string): ValidationError[] {
	const errors: ValidationError[] = [];
	const properties = schemaLike.properties;

	for (const key of Object.keys(data)) {
		if (!(key in properties)) {
			errors.push({ path: joinPath(pathPrefix, key), message: `Unknown property "${key}"` });
		}
	}

	for (const key of schemaLike.required ?? []) {
		if (!(key in data) || data[key] === undefined) {
			errors.push({ path: joinPath(pathPrefix, key), message: 'Required property is missing' });
		}
	}

	for (const [key, propSchema] of Object.entries(properties)) {
		if (data[key] === undefined) {
			continue;
		}
		errors.push(...validateProperty(propSchema, data[key], joinPath(pathPrefix, key)));
	}

	return errors;
}

function validateProperty(propSchema: JsonSchemaProperty, value: unknown, path: string): ValidationError[] {
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
			return validateRange(propSchema, value, path);
		case 'number':
			if (typeof value !== 'number' || !Number.isFinite(value)) {
				return [{ path, message: 'Expected a number' }];
			}
			return validateRange(propSchema, value, path);
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
			const errors: ValidationError[] = [];
			value.forEach((item, index) => {
				const itemPath = `${path}[${index}]`;
				if (itemSchema.type === 'object') {
					if (typeof item !== 'object' || item === null || Array.isArray(item)) {
						errors.push({ path: itemPath, message: 'Expected an object' });
					} else {
						errors.push(...validateObject(
							{ properties: itemSchema.properties ?? {}, required: itemSchema.required },
							item as Record<string, unknown>,
							itemPath
						));
					}
				} else {
					errors.push(...validateProperty(itemSchema, item, itemPath));
				}
			});
			return errors;
		}
		default:
			return [];
	}
}

function validateRange(propSchema: JsonSchemaProperty, value: number, path: string): ValidationError[] {
	const errors: ValidationError[] = [];
	if (propSchema.minimum !== undefined && value < propSchema.minimum) {
		errors.push({ path, message: `Must be >= ${propSchema.minimum}` });
	}
	if (propSchema.maximum !== undefined && value > propSchema.maximum) {
		errors.push({ path, message: `Must be <= ${propSchema.maximum}` });
	}
	return errors;
}

function joinPath(prefix: string, key: string): string {
	return prefix ? `${prefix}.${key}` : key;
}
