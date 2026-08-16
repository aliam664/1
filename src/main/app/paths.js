import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));

/** Repository / app root (two levels above src/main/app). */
export const APP_ROOT = path.resolve(HERE, '../../..');

export const RENDERER_DIR = path.join(APP_ROOT, 'src', 'renderer');
export const RENDERER_INDEX = path.join(RENDERER_DIR, 'index.html');
export const PRELOAD_PATH = path.join(APP_ROOT, 'src', 'preload', 'index.cjs');

/**
 * @param {{ getPath: (name: string) => string }} app
 */
export function resolveUserPaths(app) {
  const userData = app.getPath('userData');
  return {
    userData,
    logsDir: path.join(userData, 'logs'),
    settingsFile: path.join(userData, 'settings.json'),
    databaseFile: path.join(userData, 'stockcorsa.db'),
    catalogCacheDir: path.join(userData, 'cache'),
    tempDir: path.join(userData, 'temp')
  };
}
