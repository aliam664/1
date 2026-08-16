/**
 * Shared IPC contract. CommonJS so the sandboxed preload can require it
 * and the ESM main process can re-export it via createRequire.
 */

const INVOKE_CHANNELS = Object.freeze({
  APP_GET_INFO: 'app:getInfo',
  SETTINGS_GET: 'settings:get',
  SETTINGS_SET_LANGUAGE: 'settings:setLanguage',
  SETTINGS_SET_THEME: 'settings:setTheme',
  SETTINGS_SET_REDUCED_MOTION: 'settings:setReducedMotion',
  SETTINGS_UPDATE: 'settings:update',

  CATALOG_GET: 'catalog:get',
  CATALOG_SYNC: 'catalog:sync',
  CATALOG_GET_ITEM: 'catalog:getItem',

  GAME_GET_STATUS: 'game:getStatus',
  GAME_DETECT: 'game:detect',
  GAME_SET_PATH: 'game:setPath',
  GAME_BROWSE: 'game:browse',

  DOWNLOADS_LIST: 'downloads:list',
  DOWNLOADS_ENQUEUE: 'downloads:enqueue',
  DOWNLOADS_PAUSE: 'downloads:pause',
  DOWNLOADS_RESUME: 'downloads:resume',
  DOWNLOADS_CANCEL: 'downloads:cancel',
  DOWNLOADS_RETRY: 'downloads:retry',

  INSTALL_ANALYZE: 'install:analyze',
  INSTALL_COMMIT: 'install:commit',
  INSTALL_UNINSTALL: 'install:uninstall',
  INSTALL_LIST: 'install:list',

  FAVORITES_LIST: 'favorites:list',
  FAVORITES_TOGGLE: 'favorites:toggle',

  UPDATES_CHECK: 'updates:check',

  SHELL_OPEN_EXTERNAL: 'shell:openExternal',
  DIALOG_SELECT_DIRECTORY: 'dialog:selectDirectory',
  DIALOG_SELECT_FILE: 'dialog:selectFile'
});

const EVENT_CHANNELS = Object.freeze({
  SETTINGS_CHANGED: 'settings:changed',
  CATALOG_UPDATED: 'catalog:updated',
  DOWNLOAD_PROGRESS: 'download:progress',
  INSTALL_PROGRESS: 'install:progress'
});

const ALLOWED_INVOKE = Object.freeze(Object.values(INVOKE_CHANNELS));
const ALLOWED_EVENTS = Object.freeze(Object.values(EVENT_CHANNELS));

function isAllowedInvoke(channel) {
  return ALLOWED_INVOKE.includes(channel);
}

function isAllowedEvent(channel) {
  return ALLOWED_EVENTS.includes(channel);
}

module.exports = {
  INVOKE_CHANNELS,
  EVENT_CHANNELS,
  ALLOWED_INVOKE,
  ALLOWED_EVENTS,
  isAllowedInvoke,
  isAllowedEvent
};
