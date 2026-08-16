import { APP_CONFIG } from '../config/appConfig.js';
import { INVOKE_CHANNELS, EVENT_CHANNELS } from './channels.js';
import { assertBoolean, assertLanguage, assertTheme, IpcValidationError } from './validate.js';

/**
 * @param {unknown} error
 * @param {{ error: Function }} logger
 */
function toEnvelope(error, logger) {
  if (error instanceof IpcValidationError) {
    logger.warn('ipc validation failed', { code: error.code, field: error.field, message: error.message });
    return { ok: false, error: error.toPublic() };
  }
  const err = /** @type {Error} */ (error);
  logger.error('ipc handler failed', { name: err.name, message: err.message });
  return { ok: false, error: { code: 'INTERNAL', message: 'Internal error' } };
}

/**
 * @param {{
 *   ipcMain: { handle: Function },
 *   BrowserWindow: { getAllWindows: () => Array<{ webContents: { send: Function } }> },
 *   settingsStore: { get: Function, update: Function },
 *   logger: { info: Function, warn: Function, error: Function }
 * }} deps
 */
export function registerIpcHandlers(deps) {
  const { ipcMain, BrowserWindow, settingsStore, logger } = deps;

  /**
   * @param {string} channel
   * @param {(event: unknown, ...args: unknown[]) => unknown} handler
   */
  function handle(channel, handler) {
    ipcMain.handle(channel, async (event, ...args) => {
      try {
        const data = await handler(event, ...args);
        return { ok: true, data };
      } catch (error) {
        return toEnvelope(error, logger);
      }
    });
  }

  function broadcastSettings(settings) {
    for (const win of BrowserWindow.getAllWindows()) {
      win.webContents.send(EVENT_CHANNELS.SETTINGS_CHANGED, settings);
    }
  }

  handle(INVOKE_CHANNELS.APP_GET_INFO, () => ({
    name: APP_CONFIG.productName,
    version: APP_CONFIG.version,
    channel: APP_CONFIG.channel,
    appId: APP_CONFIG.appId,
    electron: process.versions.electron || null,
    chrome: process.versions.chrome || null,
    node: process.versions.node,
    platform: process.platform,
    arch: process.arch
  }));

  handle(INVOKE_CHANNELS.SETTINGS_GET, () => settingsStore.get());

  handle(INVOKE_CHANNELS.SETTINGS_SET_LANGUAGE, (_event, language) => {
    const next = settingsStore.update({ language: assertLanguage(language) });
    broadcastSettings(next);
    return next;
  });

  handle(INVOKE_CHANNELS.SETTINGS_SET_THEME, (_event, theme) => {
    const next = settingsStore.update({ theme: assertTheme(theme) });
    broadcastSettings(next);
    return next;
  });

  handle(INVOKE_CHANNELS.SETTINGS_SET_REDUCED_MOTION, (_event, value) => {
    const next = settingsStore.update({ reducedMotion: assertBoolean(value, 'reducedMotion') });
    broadcastSettings(next);
    return next;
  });

  logger.info('ipc handlers registered', { channels: Object.values(INVOKE_CHANNELS) });
}
