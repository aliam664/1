const { contextBridge, ipcRenderer } = require('electron');
const {
  INVOKE_CHANNELS,
  EVENT_CHANNELS,
  isAllowedInvoke,
  isAllowedEvent
} = require('../shared/ipc-contract.cjs');

async function invoke(channel, ...args) {
  if (!isAllowedInvoke(channel)) {
    return { ok: false, error: { code: 'FORBIDDEN', message: 'Unknown IPC channel' } };
  }
  return ipcRenderer.invoke(channel, ...args);
}

function listen(channel, listener) {
  if (!isAllowedEvent(channel) || typeof listener !== 'function') {
    return () => {};
  }
  const wrapped = (_event, payload) => listener(payload);
  ipcRenderer.on(channel, wrapped);
  return () => ipcRenderer.removeListener(channel, wrapped);
}

const api = {
  isElectron: true,
  app: {
    getInfo: () => invoke(INVOKE_CHANNELS.APP_GET_INFO)
  },
  settings: {
    get: () => invoke(INVOKE_CHANNELS.SETTINGS_GET),
    setLanguage: (language) => invoke(INVOKE_CHANNELS.SETTINGS_SET_LANGUAGE, language),
    setTheme: (theme) => invoke(INVOKE_CHANNELS.SETTINGS_SET_THEME, theme),
    setReducedMotion: (value) => invoke(INVOKE_CHANNELS.SETTINGS_SET_REDUCED_MOTION, value),
    update: (patch) => invoke(INVOKE_CHANNELS.SETTINGS_UPDATE, patch),
    onChanged: (listener) => listen(EVENT_CHANNELS.SETTINGS_CHANGED, listener)
  },
  catalog: {
    get: () => invoke(INVOKE_CHANNELS.CATALOG_GET),
    sync: (force) => invoke(INVOKE_CHANNELS.CATALOG_SYNC, force),
    getItem: (id) => invoke(INVOKE_CHANNELS.CATALOG_GET_ITEM, id),
    onUpdated: (listener) => listen(EVENT_CHANNELS.CATALOG_UPDATED, listener)
  },
  game: {
    getStatus: () => invoke(INVOKE_CHANNELS.GAME_GET_STATUS),
    detect: () => invoke(INVOKE_CHANNELS.GAME_DETECT),
    setPath: (gamePath) => invoke(INVOKE_CHANNELS.GAME_SET_PATH, gamePath),
    browse: () => invoke(INVOKE_CHANNELS.GAME_BROWSE),
    launch: () => invoke(INVOKE_CHANNELS.GAME_LAUNCH)
  },
  downloads: {
    list: () => invoke(INVOKE_CHANNELS.DOWNLOADS_LIST),
    enqueue: (contentId) => invoke(INVOKE_CHANNELS.DOWNLOADS_ENQUEUE, contentId),
    pause: (id) => invoke(INVOKE_CHANNELS.DOWNLOADS_PAUSE, id),
    resume: (id) => invoke(INVOKE_CHANNELS.DOWNLOADS_RESUME, id),
    cancel: (id) => invoke(INVOKE_CHANNELS.DOWNLOADS_CANCEL, id),
    retry: (id) => invoke(INVOKE_CHANNELS.DOWNLOADS_RETRY, id),
    onProgress: (listener) => listen(EVENT_CHANNELS.DOWNLOAD_PROGRESS, listener)
  },
  install: {
    analyze: (payload) => invoke(INVOKE_CHANNELS.INSTALL_ANALYZE, payload),
    commit: (plan) => invoke(INVOKE_CHANNELS.INSTALL_COMMIT, plan),
    uninstall: (id) => invoke(INVOKE_CHANNELS.INSTALL_UNINSTALL, id),
    list: () => invoke(INVOKE_CHANNELS.INSTALL_LIST),
    onProgress: (listener) => listen(EVENT_CHANNELS.INSTALL_PROGRESS, listener)
  },
  favorites: {
    list: () => invoke(INVOKE_CHANNELS.FAVORITES_LIST),
    toggle: (payload) => invoke(INVOKE_CHANNELS.FAVORITES_TOGGLE, payload)
  },
  updates: {
    check: () => invoke(INVOKE_CHANNELS.UPDATES_CHECK)
  },
  shell: {
    openExternal: (url) => invoke(INVOKE_CHANNELS.SHELL_OPEN_EXTERNAL, url)
  },
  dialog: {
    selectDirectory: () => invoke(INVOKE_CHANNELS.DIALOG_SELECT_DIRECTORY),
    selectFile: () => invoke(INVOKE_CHANNELS.DIALOG_SELECT_FILE)
  },
  on: listen
};

contextBridge.exposeInMainWorld('stockcorsa', Object.freeze(api));
