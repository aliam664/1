import { app, BrowserWindow, dialog, ipcMain, protocol, shell } from 'electron';
import { existsSync, mkdirSync, readFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import { APP_CONFIG } from '../config/appConfig.js';
import { detectAssettoCorsa } from '../assetto-corsa/detector.js';
import { createArchiveService } from '../archive/archiveService.js';
import { createCatalogService } from '../catalog/catalogService.js';
import { openDatabase } from '../database/adapter.js';
import { applyMigrations } from '../database/migrations.js';
import { createRepositories } from '../database/repositories.js';
import { createDownloadManager } from '../downloader/downloadManager.js';
import { createHttpClient } from '../http/httpClient.js';
import { createInstaller } from '../installer/installer.js';
import { registerIpcHandlers } from '../ipc/registerIpc.js';
import { EVENT_CHANNELS } from '../ipc/channels.js';
import { createLogger } from '../logger/logger.js';
import { createSettingsStore } from '../settings/settingsStore.js';
import { createUpdater } from '../updater/updater.js';
import { APP_ROOT, resolveUserPaths } from './paths.js';
import { createMainWindow } from './window.js';

protocol.registerSchemesAsPrivileged([
  { scheme: 'stockcorsa', privileges: { standard: true, secure: true, supportFetchAPI: false } }
]);

const gotLock = app.requestSingleInstanceLock();
if (!gotLock) {
  app.quit();
} else {
  bootstrap();
}

function bootstrap() {
  app.setName(APP_CONFIG.productName);
  app.setAppUserModelId(APP_CONFIG.appId);

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

  app.whenReady().then(start).catch((error) => {
    dialog.showErrorBox('StockCorsa', error instanceof Error ? error.message : 'Failed to start');
    app.quit();
  });
}

async function start() {
  const userPaths = resolveUserPaths(app);
  mkdirSync(userPaths.tempDir, { recursive: true });
  const logger = createLogger(userPaths.logsDir);
  const db = await openDatabase(userPaths.databaseFile, logger);
  applyMigrations(db, logger);
  const repos = createRepositories(db);
  const settingsStore = createSettingsStore({
    repos,
    logger,
    legacyFilePath: userPaths.settingsFile
  });

  const http = createHttpClient({ logger, userAgent: APP_CONFIG.userAgent });
  const catalog = createCatalogService({
    cacheDir: userPaths.catalogCacheDir,
    http,
    repos,
    logger,
    overrideManifestUrl: settingsStore.get().catalogOverrideUrl || undefined
  });
  seedBundledCatalog(catalog, logger);

  const downloads = createDownloadManager({
    repos,
    tempDir: path.join(userPaths.tempDir, 'downloads'),
    logger,
    getConcurrency: () => settingsStore.get().maxConcurrentDownloads,
    onProgress: (payload) => {
      for (const win of BrowserWindow.getAllWindows()) {
        win.webContents.send(EVENT_CHANNELS.DOWNLOAD_PROGRESS, payload);
      }
    }
  });

  const archive = createArchiveService({
    logger,
    get externalToolPath() {
      return settingsStore.get().externalToolPath;
    },
    onProgress: (payload) => {
      for (const win of BrowserWindow.getAllWindows()) {
        win.webContents.send(EVENT_CHANNELS.INSTALL_PROGRESS, payload);
      }
    }
  });

  const installer = createInstaller({
    repos,
    archive,
    tempDir: path.join(userPaths.tempDir, 'extract'),
    logger,
    getGamePath: () => settingsStore.get().gamePath,
    onProgress: (payload) => {
      for (const win of BrowserWindow.getAllWindows()) {
        win.webContents.send(EVENT_CHANNELS.INSTALL_PROGRESS, payload);
      }
    }
  });

  const updater = createUpdater({
    http,
    get channel() {
      return settingsStore.get().updateChannel;
    }
  });

  registerIpcHandlers({
    ipcMain,
    BrowserWindow,
    dialog,
    shell,
    settingsStore,
    catalog,
    detector: { detectAssettoCorsa },
    downloads,
    installer,
    updater,
    repos,
    logger
  });

  downloads.restore();
  cleanupTemp(userPaths.tempDir, logger);

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) {
      createMainWindow({ logger });
    }
  });

  logger.info('application ready', {
    version: APP_CONFIG.version,
    engine: db.engine,
    platform: process.platform
  });
  createMainWindow({ logger });

  catalog.sync(false).catch((error) => {
    logger.warn('startup catalog sync failed', { message: error.message });
  });

  app.on('before-quit', () => {
    logger.info('application quitting');
    db.close();
  });
}

function seedBundledCatalog(catalog, logger) {
  if (catalog.hasCache()) {
    return;
  }
  const candidates = [
    path.join(APP_ROOT, 'dist', 'catalog.json'),
    path.join(APP_ROOT, 'src', 'renderer', 'services', 'demo-catalog.json')
  ];
  for (const file of candidates) {
    if (!existsSync(file)) {
      continue;
    }
    try {
      catalog.seed(JSON.parse(readFileSync(file, 'utf8')));
      logger.info('seeded bundled catalog', { file });
      return;
    } catch (error) {
      logger.warn('bundled catalog seed failed', { file, message: error.message });
    }
  }
}

function cleanupTemp(tempDir, logger) {
  try {
    if (existsSync(tempDir)) {
      rmSync(path.join(tempDir, 'extract'), { recursive: true, force: true });
    }
  } catch (error) {
    logger.warn('temp cleanup failed', { message: error.message });
  }
}
