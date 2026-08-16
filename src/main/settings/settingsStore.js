import { existsSync, readFileSync } from 'node:fs';
import {
  assertBoolean,
  assertBoundedString,
  assertEnum,
  assertLanguage,
  assertTheme,
  IpcValidationError
} from '../ipc/validate.js';
import { isTrustedHttpsUrl } from '../config/endpoints.js';

/**
 * @typedef {object} Settings
 * @property {'fa' | 'en'} language
 * @property {'dark' | 'light'} theme
 * @property {boolean} reducedMotion
 * @property {string} gamePath
 * @property {number} maxConcurrentDownloads
 * @property {string} externalToolPath
 * @property {string} catalogOverrideUrl
 * @property {'stable' | 'beta'} updateChannel
 */

export const DEFAULT_SETTINGS = Object.freeze({
  language: 'fa',
  theme: 'dark',
  reducedMotion: false,
  gamePath: '',
  maxConcurrentDownloads: 2,
  externalToolPath: '',
  catalogOverrideUrl: '',
  updateChannel: 'stable'
});

const KEYS = Object.keys(DEFAULT_SETTINGS);

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
  if (input.gamePath !== undefined) {
    next.gamePath = assertBoundedString(input.gamePath, 'gamePath', { min: 0, max: 1024 });
  }
  if (input.maxConcurrentDownloads !== undefined) {
    const n = Number(input.maxConcurrentDownloads);
    if (!Number.isInteger(n) || n < 1 || n > 4) {
      throw new IpcValidationError('maxConcurrentDownloads must be 1–4', {
        field: 'maxConcurrentDownloads',
        code: 'RANGE'
      });
    }
    next.maxConcurrentDownloads = n;
  }
  if (input.externalToolPath !== undefined) {
    next.externalToolPath = assertBoundedString(input.externalToolPath, 'externalToolPath', {
      min: 0,
      max: 1024
    });
  }
  if (input.catalogOverrideUrl !== undefined) {
    const url = assertBoundedString(input.catalogOverrideUrl, 'catalogOverrideUrl', {
      min: 0,
      max: 1024
    });
    if (url && !isTrustedHttpsUrl(url) && !url.startsWith('https://')) {
      throw new IpcValidationError('catalogOverrideUrl must be https', {
        field: 'catalogOverrideUrl',
        code: 'URL'
      });
    }
    if (url && !url.startsWith('https://')) {
      throw new IpcValidationError('catalogOverrideUrl must be https', {
        field: 'catalogOverrideUrl',
        code: 'URL'
      });
    }
    next.catalogOverrideUrl = url;
  }
  if (input.updateChannel !== undefined) {
    next.updateChannel = assertEnum(input.updateChannel, ['stable', 'beta'], 'updateChannel');
  }
  return next;
}

function encode(value) {
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function decode(key, raw) {
  if (raw == null) {
    return DEFAULT_SETTINGS[key];
  }
  const fallback = DEFAULT_SETTINGS[key];
  if (typeof fallback === 'boolean') {
    return raw === 'true' || raw === true;
  }
  if (typeof fallback === 'number') {
    const n = Number(raw);
    return Number.isFinite(n) ? n : fallback;
  }
  return String(raw);
}

/**
 * @param {{ repos: ReturnType<import('../database/repositories.js').createRepositories>, logger: { info: Function, warn: Function, error: Function }, legacyFilePath?: string }} options
 */
export function createSettingsStore(options) {
  const { repos, logger, legacyFilePath } = options;

  function readAll() {
    const rows = repos.settings.getAll();
    /** @type {Record<string, unknown>} */
    const raw = {};
    for (const key of KEYS) {
      if (rows[key] !== undefined) {
        raw[key] = decode(key, rows[key]);
      }
    }
    return normalizeSettings(raw);
  }

  if (legacyFilePath && existsSync(legacyFilePath) && !repos.settings.get('language')) {
    try {
      const legacy = normalizeSettings(JSON.parse(readFileSync(legacyFilePath, 'utf8')));
      for (const key of KEYS) {
        repos.settings.set(key, encode(legacy[key]));
      }
      logger.info('imported legacy JSON settings into sqlite');
    } catch (error) {
      logger.warn('legacy settings import failed', {
        message: error instanceof Error ? error.message : String(error)
      });
    }
  }

  if (!repos.settings.get('language')) {
    for (const key of KEYS) {
      repos.settings.set(key, encode(DEFAULT_SETTINGS[key]));
    }
  }

  return {
    get() {
      return readAll();
    },
    /**
     * @param {Partial<Settings>} patch
     */
    update(patch) {
      if (patch == null || typeof patch !== 'object' || Array.isArray(patch)) {
        throw new IpcValidationError('Expected a settings object', { field: 'settings', code: 'TYPE' });
      }
      const merged = normalizeSettings({ ...readAll(), ...patch });
      for (const key of KEYS) {
        repos.settings.set(key, encode(merged[key]));
      }
      logger.info('settings updated', { language: merged.language, theme: merged.theme });
      return { ...merged };
    }
  };
}
