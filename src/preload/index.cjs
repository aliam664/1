const { contextBridge, ipcRenderer } = require('electron');
const {
  INVOKE_CHANNELS,
  EVENT_CHANNELS,
  isAllowedInvoke,
  isAllowedEvent
} = require('../shared/ipc-contract.cjs');

/**
 * Preload runs sandboxed. We expose a frozen, explicit API.
 * ipcRenderer is never handed to the page.
 */

/**
 * @param {string} channel
 * @param  {...unknown} args
 */
async function invoke(channel, ...args) {
  if (!isAllowedInvoke(channel)) {
    return { ok: false, error: { code: 'FORBIDDEN', message: 'Unknown IPC channel' } };
  }
  return ipcRenderer.invoke(channel, ...args);
}

const api = {
  isElectron: true,
  app: {
    getInfo() {
      return invoke(INVOKE_CHANNELS.APP_GET_INFO);
    }
  },
  settings: {
    get() {
      return invoke(INVOKE_CHANNELS.SETTINGS_GET);
    },
    setLanguage(language) {
      return invoke(INVOKE_CHANNELS.SETTINGS_SET_LANGUAGE, language);
    },
    setTheme(theme) {
      return invoke(INVOKE_CHANNELS.SETTINGS_SET_THEME, theme);
    },
    setReducedMotion(value) {
      return invoke(INVOKE_CHANNELS.SETTINGS_SET_REDUCED_MOTION, value);
    },
    /**
     * @param {(settings: unknown) => void} listener
     * @returns {() => void}
     */
    onChanged(listener) {
      if (typeof listener !== 'function') {
        return () => {};
      }
      const wrapped = (_event, settings) => {
        listener(settings);
      };
      ipcRenderer.on(EVENT_CHANNELS.SETTINGS_CHANGED, wrapped);
      return () => {
        ipcRenderer.removeListener(EVENT_CHANNELS.SETTINGS_CHANGED, wrapped);
      };
    }
  },
  /**
   * Subscribe to a main-process event. Only allow-listed channels work.
   * @param {string} channel
   * @param {(payload: unknown) => void} listener
   * @returns {() => void}
   */
  on(channel, listener) {
    if (!isAllowedEvent(channel) || typeof listener !== 'function') {
      return () => {};
    }
    const wrapped = (_event, payload) => listener(payload);
    ipcRenderer.on(channel, wrapped);
    return () => ipcRenderer.removeListener(channel, wrapped);
  }
};

contextBridge.exposeInMainWorld('stockcorsa', Object.freeze(api));
