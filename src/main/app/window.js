import { BrowserWindow, session, shell } from 'electron';
import { pathToFileURL } from 'node:url';
import { CONTENT_SECURITY_POLICY, PERMISSIONS_DENIED, WEB_PREFERENCES } from './securityPolicy.js';
import { PRELOAD_PATH, RENDERER_INDEX } from './paths.js';

/**
 * @param {string} url
 * @returns {boolean}
 */
export function isSafeExternalUrl(url) {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:';
  } catch {
    return false;
  }
}

/**
 * Attach navigation / permission / CSP locks to a session.
 * Extracted so tests can reason about the policy without opening a window.
 *
 * @param {Electron.Session} targetSession
 * @param {{ warn: Function }} logger
 */
export function hardenSession(targetSession, logger) {
  targetSession.webRequest.onHeadersReceived((details, callback) => {
    const headers = { ...details.responseHeaders };
    headers['Content-Security-Policy'] = [CONTENT_SECURITY_POLICY];
    callback({ responseHeaders: headers });
  });

  targetSession.setPermissionRequestHandler((_webContents, permission, callback) => {
    logger.warn('denied renderer permission request', { permission });
    callback(false);
  });

  targetSession.setPermissionCheckHandler((_webContents, permission) => {
    if (PERMISSIONS_DENIED.includes(permission)) {
      return false;
    }
    return false;
  });
}

/**
 * @param {{ logger: { info: Function, warn: Function, error: Function } }} options
 * @returns {Electron.BrowserWindow}
 */
export function createMainWindow(options) {
  const { logger } = options;
  hardenSession(session.defaultSession, logger);

  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    minWidth: 960,
    minHeight: 640,
    show: false,
    backgroundColor: '#080B11',
    autoHideMenuBar: true,
    webPreferences: {
      ...WEB_PREFERENCES,
      preload: PRELOAD_PATH
    }
  });

  win.webContents.setWindowOpenHandler(({ url }) => {
    if (isSafeExternalUrl(url)) {
      shell.openExternal(url).catch((error) => {
        logger.warn('openExternal failed', { message: error.message });
      });
    } else {
      logger.warn('blocked window open', { url });
    }
    return { action: 'deny' };
  });

  const allowedIndex = pathToFileURL(RENDERER_INDEX).href;

  win.webContents.on('will-navigate', (event, url) => {
    if (url !== allowedIndex && !url.startsWith(`${allowedIndex}#`)) {
      event.preventDefault();
      logger.warn('blocked renderer navigation', { url });
    }
  });

  win.webContents.on('will-attach-webview', (event) => {
    event.preventDefault();
    logger.warn('blocked webview attach');
  });

  win.once('ready-to-show', () => {
    win.show();
  });

  win.loadFile(RENDERER_INDEX).catch((error) => {
    logger.error('failed to load renderer', { message: error.message });
  });

  return win;
}
