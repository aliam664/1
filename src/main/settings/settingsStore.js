import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { assertBoolean, assertLanguage, assertTheme, IpcValidationError } from '../ipc/validate.js';

/**
 * @typedef {object} Settings
 * @property {'fa' | 'en'} language
 * @property {'dark' | 'light'} theme
 * @property {boolean} reducedMotion
 */

export const DEFAULT_SETTINGS = Object.freeze({
  language: 'fa',
  theme: 'dark',
  reducedMotion: false
});

/**
 * @param {unknown} raw
 * @returns {Settings}
 */
export function normalizeSettings(raw) {
  if (raw == null || typeof raw !== 'object' || Array.isArray(raw)) {
    return { ...DEFAULT_SETTINGS };
  }
  const input = /** @type {Record<string, unknown>} */ (raw);
  const next = { ...DEFAULT_SETTINGS };
  if (input.language !== undefined) {
    next.language = assertLanguage(input.language);
  }
  if (input.theme !== undefined) {
    next.theme = assertTheme(input.theme);
  }
  if (input.reducedMotion !== undefined) {
    next.reducedMotion = assertBoolean(input.reducedMotion, 'reducedMotion');
  }
  return next;
}

/**
 * JSON settings store used in Stage 1.
 * Stage 2 replaces the persistence with the SQLite `settings` table behind
 * the same `get` / `update` surface so IPC does not change.
 *
 * @param {{ filePath: string, logger: { info: Function, warn: Function, error: Function } }} options
 */
export function createSettingsStore(options) {
  const { filePath, logger } = options;
  mkdirSync(path.dirname(filePath), { recursive: true });

  /** @type {Settings} */
  let current = { ...DEFAULT_SETTINGS };

  function loadFromDisk() {
    try {
      const raw = readFileSync(filePath, 'utf8');
      current = normalizeSettings(JSON.parse(raw));
    } catch (error) {
      const err = /** @type {NodeJS.ErrnoException} */ (error);
      if (err.code !== 'ENOENT') {
        logger.warn('settings file unreadable, falling back to defaults', {
          code: err.code,
          message: err.message
        });
      }
      current = { ...DEFAULT_SETTINGS };
    }
  }

  function persist() {
    const tempPath = `${filePath}.tmp`;
    writeFileSync(tempPath, `${JSON.stringify(current, null, 2)}\n`, 'utf8');
    renameSync(tempPath, filePath);
  }

  loadFromDisk();

  return {
    /**
     * @returns {Settings}
     */
    get() {
      return { ...current };
    },

    /**
     * @param {Partial<Settings>} patch
     * @returns {Settings}
     */
    update(patch) {
      if (patch == null || typeof patch !== 'object' || Array.isArray(patch)) {
        throw new IpcValidationError('Expected a settings object', { field: 'settings', code: 'TYPE' });
      }
      const merged = normalizeSettings({ ...current, ...patch });
      current = merged;
      try {
        persist();
      } catch (error) {
        const err = /** @type {Error} */ (error);
        logger.error('failed to persist settings', { message: err.message });
        throw new IpcValidationError('Could not save settings', { code: 'IO' });
      }
      logger.info('settings updated', { language: current.language, theme: current.theme });
      return { ...current };
    }
  };
}
