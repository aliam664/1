const STORAGE_KEY = 'stockcorsa.settings';

const DEFAULTS = Object.freeze({
  language: 'fa',
  theme: 'dark',
  reducedMotion: false
});

function readLocal() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return { ...DEFAULTS };
    }
    return { ...DEFAULTS, ...JSON.parse(raw) };
  } catch {
    return { ...DEFAULTS };
  }
}

function writeLocal(settings) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  return settings;
}

/**
 * Browser fallback used by the preview server. It never claims Node access.
 */
function createBrowserBridge() {
  let cached = readLocal();
  const listeners = new Set();

  function emit(next) {
    cached = next;
    writeLocal(next);
    listeners.forEach((fn) => fn(next));
    return { ok: true, data: next };
  }

  return {
    isElectron: false,
    app: {
      async getInfo() {
        return {
          ok: true,
          data: {
            name: 'StockCorsa Launcher',
            version: '1.0.0',
            channel: 'preview',
            appId: 'com.stockcorsa.launcher',
            electron: null,
            chrome: null,
            node: null,
            platform: 'web',
            arch: 'preview'
          }
        };
      }
    },
    settings: {
      async get() {
        return { ok: true, data: { ...cached } };
      },
      async setLanguage(language) {
        if (language !== 'fa' && language !== 'en') {
          return { ok: false, error: { code: 'ENUM', message: 'Invalid language' } };
        }
        return emit({ ...cached, language });
      },
      async setTheme(theme) {
        if (theme !== 'dark' && theme !== 'light') {
          return { ok: false, error: { code: 'ENUM', message: 'Invalid theme' } };
        }
        return emit({ ...cached, theme });
      },
      async setReducedMotion(value) {
        if (typeof value !== 'boolean') {
          return { ok: false, error: { code: 'TYPE', message: 'Expected boolean' } };
        }
        return emit({ ...cached, reducedMotion: value });
      },
      onChanged(listener) {
        listeners.add(listener);
        return () => listeners.delete(listener);
      }
    },
    on() {
      return () => {};
    }
  };
}

/**
 * @returns {Window['stockcorsa']}
 */
export function getBridge() {
  if (window.stockcorsa) {
    return window.stockcorsa;
  }
  const fallback = createBrowserBridge();
  window.stockcorsa = fallback;
  return fallback;
}
