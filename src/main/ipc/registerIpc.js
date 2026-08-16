import { APP_CONFIG } from '../config/appConfig.js';
import { AppError } from '../app/errors.js';
import { inspectGamePath } from '../assetto-corsa/validatePath.js';
import { INVOKE_CHANNELS, EVENT_CHANNELS } from './channels.js';
import {
  assertBoolean,
  assertBoundedString,
  assertHttpsUrl,
  assertId,
  assertLanguage,
  assertTheme,
  IpcValidationError
} from './validate.js';

/**
 * @param {unknown} error
 * @param {{ error: Function, warn: Function }} logger
 */
function toEnvelope(error, logger) {
  if (error instanceof IpcValidationError || error instanceof AppError) {
    logger.warn('ipc rejected', { code: error.code, message: error.message });
    return { ok: false, error: error.toPublic() };
  }
  const err = /** @type {Error} */ (error);
  logger.error('ipc handler failed', { name: err.name, message: err.message });
  return { ok: false, error: { code: 'INTERNAL', message: 'Internal error' } };
}

/**
 * @param {object} deps
 */
export function registerIpcHandlers(deps) {
  const {
    ipcMain,
    BrowserWindow,
    dialog,
    shell,
    settingsStore,
    catalog,
    detector,
    downloads,
    installer,
    updater,
    logger
  } = deps;

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

  function broadcast(channel, payload) {
    for (const win of BrowserWindow.getAllWindows()) {
      win.webContents.send(channel, payload);
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
    broadcast(EVENT_CHANNELS.SETTINGS_CHANGED, next);
    return next;
  });

  handle(INVOKE_CHANNELS.SETTINGS_SET_THEME, (_event, theme) => {
    const next = settingsStore.update({ theme: assertTheme(theme) });
    broadcast(EVENT_CHANNELS.SETTINGS_CHANGED, next);
    return next;
  });

  handle(INVOKE_CHANNELS.SETTINGS_SET_REDUCED_MOTION, (_event, value) => {
    const next = settingsStore.update({ reducedMotion: assertBoolean(value, 'reducedMotion') });
    broadcast(EVENT_CHANNELS.SETTINGS_CHANGED, next);
    return next;
  });

  handle(INVOKE_CHANNELS.SETTINGS_UPDATE, (_event, patch) => {
    const next = settingsStore.update(patch);
    broadcast(EVENT_CHANNELS.SETTINGS_CHANGED, next);
    return next;
  });

  handle(INVOKE_CHANNELS.CATALOG_GET, () => catalog.getSnapshot());

  handle(INVOKE_CHANNELS.CATALOG_SYNC, async (_event, force) => {
    const result = await catalog.sync(Boolean(force));
    broadcast(EVENT_CHANNELS.CATALOG_UPDATED, result.catalog);
    return result;
  });

  handle(INVOKE_CHANNELS.CATALOG_GET_ITEM, (_event, id) => {
    const item = catalog.getItem(assertId(id));
    if (!item) {
      throw new IpcValidationError('Unknown catalog item', { field: 'id', code: 'NOT_FOUND' });
    }
    return item;
  });

  handle(INVOKE_CHANNELS.GAME_GET_STATUS, () => {
    const settings = settingsStore.get();
    return settings.gamePath ? inspectGamePath(settings.gamePath) : { path: '', valid: false, missing: ['path'], writable: false, checks: [] };
  });

  handle(INVOKE_CHANNELS.GAME_DETECT, async () => {
    const settings = settingsStore.get();
    const result = await detector.detectAssettoCorsa({ savedPath: settings.gamePath });
    if (result.found) {
      const next = settingsStore.update({ gamePath: result.path });
      broadcast(EVENT_CHANNELS.SETTINGS_CHANGED, next);
    }
    return result;
  });

  handle(INVOKE_CHANNELS.GAME_SET_PATH, (_event, gamePath) => {
    const inspected = inspectGamePath(String(gamePath || ''));
    if (!inspected.valid) {
      throw new IpcValidationError(`Incomplete game path (missing ${inspected.missing.join(', ')})`, {
        field: 'gamePath',
        code: 'GAME_PATH_INVALID'
      });
    }
    const next = settingsStore.update({ gamePath: inspected.path });
    broadcast(EVENT_CHANNELS.SETTINGS_CHANGED, next);
    return inspected;
  });

  handle(INVOKE_CHANNELS.GAME_BROWSE, async () => {
    const win = BrowserWindow.getFocusedWindow();
    const result = await dialog.showOpenDialog(win || undefined, {
      properties: ['openDirectory']
    });
    if (result.canceled || !result.filePaths[0]) {
      return null;
    }
    return inspectGamePath(result.filePaths[0]);
  });

  handle(INVOKE_CHANNELS.DOWNLOADS_LIST, () => downloads.list());

  handle(INVOKE_CHANNELS.DOWNLOADS_ENQUEUE, async (_event, contentId) => {
    const item = catalog.getItem(assertId(contentId, 'contentId'));
    if (!item) {
      throw new IpcValidationError('Unknown catalog item', { field: 'contentId', code: 'NOT_FOUND' });
    }
    if (item.status === 'revoked') {
      throw new IpcValidationError(item.revocationReason || 'This item was revoked', {
        field: 'contentId',
        code: 'REVOKED'
      });
    }
    const id = await downloads.enqueue({
      contentId: item.id,
      url: item.downloadUrl,
      size: item.size,
      sha256: item.sha256,
      fileName: `${item.id}.${item.archiveType}`
    });
    return { id, item };
  });

  handle(INVOKE_CHANNELS.DOWNLOADS_PAUSE, (_event, id) => {
    downloads.pause(assertId(id));
    return downloads.list();
  });
  handle(INVOKE_CHANNELS.DOWNLOADS_RESUME, (_event, id) => {
    downloads.resume(assertId(id));
    return downloads.list();
  });
  handle(INVOKE_CHANNELS.DOWNLOADS_CANCEL, (_event, id) => {
    downloads.cancel(assertId(id));
    return downloads.list();
  });
  handle(INVOKE_CHANNELS.DOWNLOADS_RETRY, (_event, id) => {
    downloads.retry(assertId(id));
    return downloads.list();
  });

  handle(INVOKE_CHANNELS.INSTALL_ANALYZE, async (_event, payload) => {
    const downloadId = assertId(payload?.downloadId, 'downloadId');
    const row = downloads.list().find((item) => item.id === downloadId);
    if (!row || row.state !== 'completed' || !row.filePath) {
      throw new IpcValidationError('Download is not ready to install', {
        field: 'downloadId',
        code: 'NOT_FOUND'
      });
    }
    const item = row.contentId ? catalog.getItem(row.contentId) : null;
    return installer.analyze(row.filePath, item?.archiveType, payload?.password);
  });

  handle(INVOKE_CHANNELS.INSTALL_COMMIT, async (_event, plan) => {
    if (!plan || typeof plan !== 'object' || typeof plan.sessionId !== 'string') {
      throw new IpcValidationError('Invalid install plan', { field: 'plan', code: 'TYPE' });
    }
    if (!Array.isArray(plan.selections)) {
      throw new IpcValidationError('Invalid install selections', { field: 'selections', code: 'TYPE' });
    }
    const sessionId = assertId(plan.sessionId, 'sessionId');
    const selections = plan.selections.map((choice) => ({
      folderName: assertBoundedString(choice?.folderName, 'folderName', {
        min: 1,
        max: 80,
        pattern: /^[^\\/:*?"<>|\0]+$/
      }),
      overwrite: Boolean(choice?.overwrite),
      backup: Boolean(choice?.backup)
    }));
    return installer.commit({
      sessionId,
      selections,
      contentId: plan.contentId ? assertId(plan.contentId, 'contentId') : undefined,
      name: typeof plan.name === 'string' ? plan.name.slice(0, 200) : undefined,
      version: typeof plan.version === 'string' ? plan.version.slice(0, 40) : undefined,
      sourceUrl: typeof plan.sourceUrl === 'string' ? plan.sourceUrl.slice(0, 2000) : undefined,
      archiveType: typeof plan.archiveType === 'string' ? plan.archiveType.slice(0, 8) : undefined,
      checksum: typeof plan.checksum === 'string' ? plan.checksum.slice(0, 64) : undefined
    });
  });

  handle(INVOKE_CHANNELS.INSTALL_UNINSTALL, (_event, id) => installer.uninstall(assertId(id)));
  handle(INVOKE_CHANNELS.INSTALL_LIST, () => installer.listInstalled());

  handle(INVOKE_CHANNELS.FAVORITES_LIST, () => deps.repos.favorites.list());
  handle(INVOKE_CHANNELS.FAVORITES_TOGGLE, (_event, payload) => {
    const contentId = assertId(payload?.contentId, 'contentId');
    const contentType = String(payload?.contentType || 'car');
    const added = deps.repos.favorites.toggle(contentId, contentType);
    return { contentId, contentType, added };
  });

  handle(INVOKE_CHANNELS.UPDATES_CHECK, () => updater.check());

  handle(INVOKE_CHANNELS.SHELL_OPEN_EXTERNAL, async (_event, url) => {
    const safe = assertHttpsUrl(url);
    await shell.openExternal(safe);
    return true;
  });

  handle(INVOKE_CHANNELS.DIALOG_SELECT_DIRECTORY, async () => {
    const win = BrowserWindow.getFocusedWindow();
    const result = await dialog.showOpenDialog(win || undefined, { properties: ['openDirectory'] });
    return result.canceled ? null : result.filePaths[0];
  });

  handle(INVOKE_CHANNELS.DIALOG_SELECT_FILE, async () => {
    const win = BrowserWindow.getFocusedWindow();
    const result = await dialog.showOpenDialog(win || undefined, {
      properties: ['openFile'],
      filters: [{ name: 'Executables', extensions: ['exe'] }]
    });
    return result.canceled ? null : result.filePaths[0];
  });

  logger.info('ipc handlers registered', { channels: ALLOWED_COUNT() });
}

function ALLOWED_COUNT() {
  return Object.keys(INVOKE_CHANNELS).length;
}

function assertBoundedLocalPath(value) {
  if (typeof value !== 'string' || value.length < 2 || value.length > 1024 || value.includes('\0')) {
    throw new IpcValidationError('Invalid archive path', { field: 'archivePath', code: 'PATH' });
  }
  return value;
}
