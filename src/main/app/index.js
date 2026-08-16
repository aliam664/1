import { app, BrowserWindow, ipcMain } from 'electron';
import { APP_CONFIG } from '../config/appConfig.js';
import { createLogger } from '../logger/logger.js';
import { createSettingsStore } from '../settings/settingsStore.js';
import { registerIpcHandlers } from '../ipc/registerIpc.js';
import { resolveUserPaths } from './paths.js';
import { createMainWindow } from './window.js';

// Single instance: a second launch focuses the existing window.
const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  bootstrap();
}

function bootstrap() {
  app.setName(APP_CONFIG.productName);
  app.setAppUserModelId(APP_CONFIG.appId);

  const userPaths = resolveUserPaths(app);
  const logger = createLogger(userPaths.logsDir);
  const settingsStore = createSettingsStore({
    filePath: userPaths.settingsFile,
    logger
  });

  registerIpcHandlers({
    ipcMain,
    BrowserWindow,
    settingsStore,
    logger
  });

  app.on('second-instance', () => {
    const existing = BrowserWindow.getAllWindows()[0];
    if (!existing) {
      return;
    }
    if (existing.isMinimized()) {
      existing.restore();
    }
    existing.focus();
  });

  app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
      app.quit();
    }
  });

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createMainWindow({ logger });
    }
  });

  app.whenReady().then(() => {
    logger.info('application ready', {
      version: APP_CONFIG.version,
      electron: process.versions.electron,
      platform: process.platform
    });
    createMainWindow({ logger });
  }).catch((error) => {
    logger.error('application failed to start', { message: error.message });
    app.quit();
  });

  app.on('before-quit', () => {
    logger.info('application quitting');
  });
}
