/**
 * Shared IPC / settings validators. Throw IpcValidationError on failure.
 * Callers in Main catch this type and return a safe error envelope.
 */

export class IpcValidationError extends Error {
  /**
   * @param {string} message
   * @param {{ field?: string, code?: string }} [meta]
   */
  constructor(message, meta = {}) {
    super(message);
    this.name = 'IpcValidationError';
    this.field = meta.field || '';
    this.code = meta.code || 'VALIDATION';
  }

  toPublic() {
    return { code: this.code, message: this.message, field: this.field };
  }
}

const LANGUAGE_VALUES = Object.freeze(['fa', 'en']);
const THEME_VALUES = Object.freeze(['dark', 'light']);

/**
 * @param {unknown} value
 * @param {string} field
 * @returns {string}
 */
export function assertString(value, field) {
  if (typeof value !== 'string') {
    throw new IpcValidationError(`Expected string for ${field}`, { field, code: 'TYPE' });
  }
  if (value.includes('\0')) {
    throw new IpcValidationError(`NUL byte is not allowed in ${field}`, { field, code: 'NUL' });
  }
  return value;
}

/**
 * @param {unknown} value
 * @param {string} field
 * @param {{ min?: number, max?: number, pattern?: RegExp }} [opts]
 * @returns {string}
 */
export function assertBoundedString(value, field, opts = {}) {
  const text = assertString(value, field);
  const min = opts.min ?? 0;
  const max = opts.max ?? 256;
  if (text.length < min || text.length > max) {
    throw new IpcValidationError(`${field} length must be between ${min} and ${max}`, {
      field,
      code: 'LENGTH'
    });
  }
  if (opts.pattern && !opts.pattern.test(text)) {
    throw new IpcValidationError(`${field} has an invalid format`, { field, code: 'PATTERN' });
  }
  return text;
}

/**
 * @template T
 * @param {unknown} value
 * @param {readonly T[]} allowed
 * @param {string} field
 * @returns {T}
 */
export function assertEnum(value, allowed, field) {
  if (!allowed.includes(/** @type {T} */ (value))) {
    throw new IpcValidationError(`Invalid value for ${field}`, { field, code: 'ENUM' });
  }
  return /** @type {T} */ (value);
}

/**
 * @param {unknown} value
 * @param {string} field
 * @returns {boolean}
 */
export function assertBoolean(value, field) {
  if (typeof value !== 'boolean') {
    throw new IpcValidationError(`Expected boolean for ${field}`, { field, code: 'TYPE' });
  }
  return value;
}

/**
 * @param {unknown} value
 * @returns {'fa' | 'en'}
 */
export function assertLanguage(value) {
  return assertEnum(value, LANGUAGE_VALUES, 'language');
}

/**
 * @param {unknown} value
 * @returns {'dark' | 'light'}
 */
export function assertTheme(value) {
  return assertEnum(value, THEME_VALUES, 'theme');
}

export const LANGUAGE_VALUES_LIST = LANGUAGE_VALUES;
export const THEME_VALUES_LIST = THEME_VALUES;
