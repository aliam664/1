import demoCatalog from './demo-catalog.json' with { type: 'json' };

const STORAGE = 'stockcorsa.v2';

const DEFAULTS = Object.freeze({
  language: 'fa',
  theme: 'dark',
  reducedMotion: false,
  gamePath: '',
  maxConcurrentDownloads: 2,
  externalToolPath: '',
  catalogOverrideUrl: '',
  updateChannel: 'stable'
});

function load() {
  try {
    return { ...DEFAULTS, ...JSON.parse(localStorage.getItem(STORAGE) || '{}') };
  } catch {
    return { ...DEFAULTS };
  }
}

function save(data) {
  localStorage.setItem(STORAGE, JSON.stringify(data));
}

function createBrowserBridge() {
  let settings = load();
  const favorites = new Set(settings.favorites || []);
  const installed = new Set(settings.installed || []);
  /** @type {object[]} */
  let downloads = [];
  const settingListeners = new Set();
  const catalogListeners = new Set();
  const downloadListeners = new Set();

  function persist() {
    save({ ...settings, favorites: [...favorites], installed: [...installed] });
  }

  function snapshotCatalog() {
    return {
      catalogVersion: demoCatalog.catalogVersion,
      generatedAt: demoCatalog.generatedAt,
      featured: demoCatalog.featured,
      categories: demoCatalog.categories,
      items: demoCatalog.items.map((item) => ({
        ...item,
        favorite: favorites.has(item.id),
        installed: installed.has(item.id),
        verified: Boolean(item.sha256)
      })),
      itemCount: demoCatalog.items.length,
      offline: false
    };
  }

  function emitSettings() {
    settingListeners.forEach((fn) => fn(settings));
    return { ok: true, data: { ...settings } };
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
            platform: 'web',
            arch: 'preview'
          }
        };
      }
    },
    settings: {
      async get() {
        return { ok: true, data: { ...settings } };
      },
      async setLanguage(language) {
        settings = { ...settings, language };
        persist();
        return emitSettings();
      },
      async setTheme(theme) {
        settings = { ...settings, theme };
        persist();
        return emitSettings();
      },
      async setReducedMotion(value) {
        settings = { ...settings, reducedMotion: value };
        persist();
        return emitSettings();
      },
      async update(patch) {
        settings = { ...settings, ...patch };
        persist();
        return emitSettings();
      },
      onChanged(listener) {
        settingListeners.add(listener);
        return () => settingListeners.delete(listener);
      }
    },
    catalog: {
      async get() {
        return { ok: true, data: snapshotCatalog() };
      },
      async sync() {
        const data = snapshotCatalog();
        catalogListeners.forEach((fn) => fn(data));
        return { ok: true, data: { status: 'cache', catalog: data, source: 'preview' } };
      },
      async getItem(id) {
        const item = snapshotCatalog().items.find((entry) => entry.id === id);
        return item ? { ok: true, data: item } : { ok: false, error: { code: 'NOT_FOUND', message: 'Unknown item' } };
      },
      onUpdated(listener) {
        catalogListeners.add(listener);
        return () => catalogListeners.delete(listener);
      }
    },
    game: {
      async getStatus() {
        return {
          ok: true,
          data: settings.gamePath
            ? { path: settings.gamePath, valid: settings.gamePath.length > 3, missing: [], writable: true, checks: [] }
            : { path: '', valid: false, missing: ['path'], writable: false, checks: [] }
        };
      },
      async detect() {
        return { ok: true, data: { found: false, path: null, source: null, attempts: [] } };
      },
      async setPath(gamePath) {
        settings = { ...settings, gamePath };
        persist();
        emitSettings();
        return { ok: true, data: { path: gamePath, valid: Boolean(gamePath), missing: [], writable: true, checks: [] } };
      },
      async browse() {
        return { ok: true, data: null };
      },
      async launch() {
        return { ok: false, error: { code: 'GAME_PATH_INVALID', message: 'Preview cannot launch Assetto Corsa' } };
      }
    },
    downloads: {
      async list() {
        return { ok: true, data: downloads };
      },
      async enqueue(contentId) {
        const item = demoCatalog.items.find((entry) => entry.id === contentId);
        if (!item) {
          return { ok: false, error: { code: 'NOT_FOUND', message: 'Unknown item' } };
        }
        const id = `dl-${contentId}`;
        if (!downloads.some((row) => row.id === id)) {
          downloads = [
            ...downloads,
            {
              id,
              contentId,
              state: 'running',
              bytesDone: 0,
              bytesTotal: item.size || 100,
              speed: 8_000_000,
              eta: 8,
              resumable: true
            }
          ];
          let done = 0;
          const timer = setInterval(() => {
            done += (item.size || 100) / 8;
            const finished = done >= (item.size || 100);
            downloads = downloads.map((row) =>
              row.id === id
                ? {
                    ...row,
                    bytesDone: Math.min(done, item.size || 100),
                    state: finished ? 'completed' : 'running',
                    eta: finished ? 0 : 4
                  }
                : row
            );
            downloadListeners.forEach((fn) => fn(downloads.find((row) => row.id === id)));
            if (finished) {
              clearInterval(timer);
              installed.add(contentId);
              persist();
            }
          }, 400);
        }
        return { ok: true, data: { id, item } };
      },
      async pause(id) {
        downloads = downloads.map((row) => (row.id === id ? { ...row, state: 'paused' } : row));
        return { ok: true, data: downloads };
      },
      async resume(id) {
        downloads = downloads.map((row) => (row.id === id ? { ...row, state: 'running' } : row));
        return { ok: true, data: downloads };
      },
      async cancel(id) {
        downloads = downloads.filter((row) => row.id !== id);
        return { ok: true, data: downloads };
      },
      async retry(id) {
        downloads = downloads.map((row) => (row.id === id ? { ...row, state: 'queued' } : row));
        return { ok: true, data: downloads };
      },
      onProgress(listener) {
        downloadListeners.add(listener);
        return () => downloadListeners.delete(listener);
      }
    },
    install: {
      async analyze() {
        return { ok: false, error: { code: 'UNRECOGNIZED_STRUCTURE', message: 'Preview cannot extract archives' } };
      },
      async commit() {
        return { ok: false, error: { code: 'GAME_PATH_INVALID', message: 'Preview cannot write to a game folder' } };
      },
      async uninstall(id) {
        installed.delete(id);
        persist();
        return { ok: true, data: { id } };
      },
      async list() {
        return { ok: true, data: [...installed].map((id) => ({ id, name: id })) };
      },
      onProgress() {
        return () => {};
      }
    },
    favorites: {
      async list() {
        return { ok: true, data: [...favorites].map((content_id) => ({ content_id })) };
      },
      async toggle(payload) {
        const id = payload.contentId;
        if (favorites.has(id)) {
          favorites.delete(id);
        } else {
          favorites.add(id);
        }
        persist();
        return { ok: true, data: { contentId: id, added: favorites.has(id) } };
      }
    },
    updates: {
      async check() {
        return { ok: true, data: { current: '1.0.0', available: false, version: '1.0.0' } };
      }
    },
    shell: {
      async openExternal() {
        return { ok: false, error: { code: 'FORBIDDEN', message: 'Preview cannot open external windows' } };
      }
    },
    dialog: {
      async selectDirectory() {
        return { ok: true, data: null };
      },
      async selectFile() {
        return { ok: true, data: null };
      }
    },
    on() {
      return () => {};
    }
  };
}

export function getBridge() {
  if (window.stockcorsa && window.stockcorsa.isElectron) {
    return window.stockcorsa;
  }
  if (window.stockcorsa && !window.stockcorsa.catalog) {
    // Stage 1 preload without new methods — wrap with preview for missing APIs.
  }
  if (!window.stockcorsa || !window.stockcorsa.catalog) {
    const fallback = createBrowserBridge();
    window.stockcorsa = fallback;
    return fallback;
  }
  return window.stockcorsa;
}
