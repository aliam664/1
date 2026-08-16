import { applyTranslations, directionFor, loadLanguage, t } from './i18n/i18n.js';
import { ALL_ROUTES, renderSidebar } from './components/sidebar.js';
import { renderHome } from './pages/home.js';
import { renderCatalogPage } from './pages/catalog.js';
import { renderFavorites } from './pages/favorites.js';
import { renderDownloads } from './pages/downloads.js';
import { renderUpdates } from './pages/updates.js';
import { renderSettings } from './pages/settings.js';
import { getBridge } from './services/bridge.js';
import { getState, setState, subscribe } from './state/store.js';

const PAGE_TITLES = {
  home: 'home.title',
  cars: 'page.cars.title',
  tracks: 'page.tracks.title',
  mods: 'page.mods.title',
  favorites: 'page.favorites.title',
  downloads: 'page.downloads.title',
  updates: 'page.updates.title',
  settings: 'settings.title'
};

const sidebarEl = document.getElementById('sidebar');
const contentEl = document.getElementById('content');
const titleEl = document.getElementById('page-title');
const toastEl = document.getElementById('toast');
const bridge = getBridge();
let toastTimer = 0;

function applyChrome(state) {
  const root = document.documentElement;
  root.lang = state.language;
  root.dir = directionFor(state.language);
  root.dataset.theme = state.theme;
  root.dataset.reducedMotion = state.reducedMotion ? 'true' : 'false';
  document.title = `${t(PAGE_TITLES[state.route] || 'home.title')} · StockCorsa`;
}

function renderPage(state) {
  contentEl.replaceChildren();
  let page;
  if (state.route === 'home') {
    page = renderHome();
  } else if (state.route === 'cars') {
    page = renderCatalogPage('car');
  } else if (state.route === 'tracks') {
    page = renderCatalogPage('track');
  } else if (state.route === 'mods') {
    page = renderCatalogPage('mod');
  } else if (state.route === 'favorites') {
    page = renderFavorites();
  } else if (state.route === 'downloads') {
    page = renderDownloads();
  } else if (state.route === 'updates') {
    page = renderUpdates();
  } else if (state.route === 'settings') {
    page = renderSettings();
  } else {
    page = renderHome();
  }
  contentEl.append(page);
  titleEl.dataset.i18n = PAGE_TITLES[state.route] || 'home.title';
  applyTranslations(document);
}

function rememberFocus() {
  const active = document.activeElement;
  if (!(active instanceof HTMLElement) || !active.closest('.app-shell')) {
    return null;
  }
  const key =
    active.getAttribute('data-search') !== null
      ? 'search'
      : active.getAttribute('data-game-path') !== null
        ? 'game-path'
        : active.getAttribute('data-nav') || active.getAttribute('data-install') || active.id;
  const start = active instanceof HTMLInputElement ? active.selectionStart : null;
  return { key, start, tag: active.tagName, name: active.getAttribute('data-search') !== null };
}

function restoreFocus(snapshot) {
  if (!snapshot) {
    return;
  }
  let node = null;
  if (snapshot.key === 'search') {
    node = document.querySelector('[data-search]');
  } else if (snapshot.key === 'game-path') {
    node = document.querySelector('[data-game-path]');
  } else if (snapshot.key) {
    node = document.querySelector(`[data-nav="${snapshot.key}"], [data-install="${snapshot.key}"]`);
  }
  if (node instanceof HTMLElement) {
    node.focus();
    if (node instanceof HTMLInputElement && snapshot.start != null) {
      node.setSelectionRange(snapshot.start, snapshot.start);
    }
  }
}

function showToast(message) {
  if (!toastEl || !message) {
    return;
  }
  toastEl.hidden = false;
  toastEl.textContent = message;
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => {
    toastEl.hidden = true;
  }, 4200);
}

function renderShell(state) {
  const focus = rememberFocus();
  const active = (state.downloads || []).filter((row) => row.state === 'running' || row.state === 'queued').length;
  sidebarEl.innerHTML = renderSidebar({
    route: state.route,
    online: state.online,
    isElectron: state.isElectron,
    downloadCount: active,
    updateCount: state.updateInfo?.available ? 1 : 0
  });
  applyChrome(state);
  renderPage(state);
  restoreFocus(focus);
}

function navigate(route) {
  if (!ALL_ROUTES.includes(route)) {
    return;
  }
  setState({ route });
  contentEl.focus();
}

async function unwrap(result, fallbackMessage) {
  if (result && result.ok) {
    return result.data;
  }
  showToast(result?.error?.message || fallbackMessage);
  return null;
}

async function refreshCatalog(force = false) {
  const result = await unwrap(await bridge.catalog.sync(force), t('error.network'));
  if (result?.catalog) {
    setState({ catalog: result.catalog });
  } else {
    const snap = await unwrap(await bridge.catalog.get(), t('error.network'));
    if (snap) {
      setState({ catalog: snap });
    }
  }
}

async function refreshDownloads() {
  const list = await unwrap(await bridge.downloads.list(), t('error.generic'));
  if (list) {
    setState({ downloads: list });
  }
}

function mergeDownload(payload) {
  if (!payload?.id) {
    return;
  }
  const current = getState().downloads || [];
  const next = current.some((row) => row.id === payload.id)
    ? current.map((row) => (row.id === payload.id ? { ...row, ...payload } : row))
    : [...current, payload];
  setState({ downloads: next.filter((row) => row.state !== 'canceled') });
  if (payload.state === 'completed' && bridge.isElectron) {
    tryInstallDownload(payload);
  }
}

async function tryInstallDownload(payload) {
  const analysis = await unwrap(await bridge.install.analyze({ downloadId: payload.id }), t('error.generic'));
  if (!analysis) {
    return;
  }
  if (!analysis.recognized || !analysis.items?.length) {
    showToast(t('install.unrecognized'));
    return;
  }
  const result = await unwrap(
    await bridge.install.commit({
      sessionId: analysis.sessionId,
      selections: analysis.items.map((item) => ({
        folderName: item.folderName,
        overwrite: false,
        backup: Boolean(item.exists)
      })),
      contentId: payload.contentId
    }),
    t('error.generic')
  );
  if (result) {
    await refreshCatalog(false);
  }
}

async function changeLanguage(language) {
  const data = await unwrap(await bridge.settings.setLanguage(language), t('error.settingsSave'));
  if (data) {
    loadLanguage(data.language);
    setState({ language: data.language, settings: data });
  }
}

async function changeTheme(theme) {
  const data = await unwrap(await bridge.settings.setTheme(theme), t('error.settingsSave'));
  if (data) {
    setState({ theme: data.theme, settings: data });
  }
}

async function changeReducedMotion(value) {
  const data = await unwrap(await bridge.settings.setReducedMotion(value), t('error.settingsSave'));
  if (data) {
    setState({ reducedMotion: data.reducedMotion, settings: data });
  }
}

async function bindEvents() {
  document.addEventListener('click', async (event) => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) {
      return;
    }
    const nav = target.closest('[data-nav]');
    if (nav) {
      navigate(nav.getAttribute('data-nav'));
      return;
    }
    const languageBtn = target.closest('[data-language]');
    if (languageBtn) {
      await changeLanguage(languageBtn.getAttribute('data-language'));
      return;
    }
    const themeBtn = target.closest('[data-theme]');
    if (themeBtn) {
      await changeTheme(themeBtn.getAttribute('data-theme'));
      return;
    }
    const motionBtn = target.closest('[data-reduced-motion]');
    if (motionBtn) {
      await changeReducedMotion(motionBtn.getAttribute('aria-pressed') !== 'true');
      return;
    }
    const installBtn = target.closest('[data-install]');
    if (installBtn) {
      await unwrap(await bridge.downloads.enqueue(installBtn.getAttribute('data-install')), t('error.generic'));
      await refreshDownloads();
      navigate('downloads');
      return;
    }
    const favBtn = target.closest('[data-fav]');
    if (favBtn) {
      await unwrap(
        await bridge.favorites.toggle({
          contentId: favBtn.getAttribute('data-fav'),
          contentType: favBtn.getAttribute('data-fav-type')
        }),
        t('error.generic')
      );
      await refreshCatalog(false);
      return;
    }
    const pause = target.closest('[data-dl-pause]');
    if (pause) {
      await unwrap(await bridge.downloads.pause(pause.getAttribute('data-dl-pause')), t('error.generic'));
      await refreshDownloads();
      return;
    }
    const resume = target.closest('[data-dl-resume]');
    if (resume) {
      await unwrap(await bridge.downloads.resume(resume.getAttribute('data-dl-resume')), t('error.generic'));
      await refreshDownloads();
      return;
    }
    const cancel = target.closest('[data-dl-cancel]');
    if (cancel) {
      await unwrap(await bridge.downloads.cancel(cancel.getAttribute('data-dl-cancel')), t('error.generic'));
      await refreshDownloads();
      return;
    }
    const retry = target.closest('[data-dl-retry]');
    if (retry) {
      await unwrap(await bridge.downloads.retry(retry.getAttribute('data-dl-retry')), t('error.generic'));
      await refreshDownloads();
      return;
    }
    const action = target.closest('[data-action]')?.getAttribute('data-action');
    if (action === 'refresh-catalog') {
      await refreshCatalog(true);
    } else if (action === 'check-updates') {
      const info = await unwrap(await bridge.updates.check(), t('error.generic'));
      if (info) {
        setState({ updateInfo: info });
      }
    } else if (action === 'detect-game') {
      const result = await unwrap(await bridge.game.detect(), t('error.gamePath'));
      const status = await unwrap(await bridge.game.getStatus(), t('error.gamePath'));
      setState({ gameStatus: result?.found ? { valid: true, path: result.path } : status });
    } else if (action === 'browse-game') {
      const picked = await unwrap(await bridge.game.browse(), t('error.gamePath'));
      if (picked?.path) {
        document.querySelector('[data-game-path]')?.setAttribute('value', picked.path);
      }
    } else if (action === 'save-game') {
      const value = document.querySelector('[data-game-path]')?.value || '';
      const status = await unwrap(await bridge.game.setPath(value), t('error.gamePath'));
      if (status) {
        setState({ gameStatus: status });
      }
    } else if (action === 'browse-tool') {
      const file = await unwrap(await bridge.dialog.selectFile(), t('error.generic'));
      if (file) {
        await unwrap(await bridge.settings.update({ externalToolPath: file }), t('error.settingsSave'));
        const settings = await unwrap(await bridge.settings.get(), t('error.generic'));
        if (settings) {
          setState({ settings });
        }
      }
    } else if (action === 'save-catalog') {
      const url = document.querySelector('[data-catalog-url]')?.value || '';
      const settings = await unwrap(await bridge.settings.update({ catalogOverrideUrl: url }), t('error.settingsSave'));
      if (settings) {
        setState({ settings });
      }
    }
    const conc = target.closest('[data-concurrency]');
    if (conc) {
      const settings = await unwrap(
        await bridge.settings.update({ maxConcurrentDownloads: Number(conc.getAttribute('data-concurrency')) }),
        t('error.settingsSave')
      );
      if (settings) {
        setState({ settings });
      }
    }
    const ext = target.closest('[data-open-url]');
    if (ext) {
      await bridge.shell.openExternal(ext.getAttribute('data-open-url'));
    }
  });

  document.addEventListener('input', (event) => {
    const target = event.target;
    if (target instanceof HTMLInputElement && target.matches('[data-search]')) {
      const query = target.value;
      const route = getState().route;
      if (query && !['cars', 'tracks', 'mods', 'favorites'].includes(route)) {
        setState({ search: query, route: 'cars' });
      } else {
        setState({ search: query });
      }
    }
  });

  window.addEventListener('online', () => setState({ online: true }));
  window.addEventListener('offline', () => setState({ online: false }));

  bridge.settings.onChanged?.((settings) => {
    if (!settings) {
      return;
    }
    if (settings.language && settings.language !== getState().language) {
      loadLanguage(settings.language);
    }
    setState({
      language: settings.language ?? getState().language,
      theme: settings.theme ?? getState().theme,
      reducedMotion: settings.reducedMotion ?? getState().reducedMotion,
      settings
    });
  });
  bridge.catalog.onUpdated?.((catalog) => {
    if (catalog) {
      setState({ catalog });
    }
  });
  bridge.downloads.onProgress?.((payload) => {
    mergeDownload(payload);
  });
}

async function boot() {
  setState({ isElectron: Boolean(bridge.isElectron), online: navigator.onLine });
  const settings = await unwrap(await bridge.settings.get(), t('error.generic'));
  if (settings) {
    loadLanguage(settings.language);
    setState({
      language: settings.language,
      theme: settings.theme,
      reducedMotion: settings.reducedMotion,
      settings
    });
  } else {
    loadLanguage('fa');
  }
  const info = await unwrap(await bridge.app.getInfo(), t('error.generic'));
  if (info) {
    setState({ appInfo: info });
  }
  const catalog = await unwrap(await bridge.catalog.get(), t('error.network'));
  if (catalog) {
    setState({ catalog });
  }
  await refreshDownloads();
  const game = await unwrap(await bridge.game.getStatus(), t('error.generic'));
  if (game) {
    setState({ gameStatus: game });
  }

  subscribe(() => renderShell(getState()));
  await bindEvents();
  renderShell(getState());
  refreshCatalog(false);
}

boot().catch((error) => {
  const pre = document.createElement('pre');
  pre.textContent = error instanceof Error ? error.message : t('error.generic');
  document.body.replaceChildren(pre);
});
