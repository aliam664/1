import { applyTranslations, directionFor, loadLanguage, t } from './i18n/i18n.js';
import { ALL_ROUTES, renderSidebar } from './components/sidebar.js';
import { renderHome } from './pages/home.js';
import { renderSettings } from './pages/settings.js';
import { renderUnavailable, UNAVAILABLE_PAGES } from './pages/unavailable.js';
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
const bridge = getBridge();

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
  } else if (state.route === 'settings') {
    page = renderSettings();
  } else if (UNAVAILABLE_PAGES[state.route]) {
    page = renderUnavailable(UNAVAILABLE_PAGES[state.route]);
  } else {
    page = renderHome();
  }
  contentEl.append(page);
  titleEl.dataset.i18n = PAGE_TITLES[state.route] || 'home.title';
  applyTranslations(document);
}

function renderShell(state) {
  sidebarEl.innerHTML = renderSidebar({
    route: state.route,
    online: state.online,
    isElectron: state.isElectron,
    downloadCount: 0,
    updateCount: 0
  });
  applyChrome(state);
  renderPage(state);
}

function navigate(route) {
  if (!ALL_ROUTES.includes(route) || getState().route === route) {
    return;
  }
  setState({ route });
  contentEl.focus();
}

async function unwrap(result, fallbackMessage) {
  if (result && result.ok) {
    return result.data;
  }
  const message = result?.error?.message || fallbackMessage;
  setState({ error: message });
  return null;
}

async function changeLanguage(language) {
  const data = await unwrap(await bridge.settings.setLanguage(language), t('error.settingsSave'));
  if (!data) {
    return;
  }
  loadLanguage(data.language);
  setState({ language: data.language });
}

async function changeTheme(theme) {
  const data = await unwrap(await bridge.settings.setTheme(theme), t('error.settingsSave'));
  if (data) {
    setState({ theme: data.theme });
  }
}

async function changeReducedMotion(value) {
  const data = await unwrap(await bridge.settings.setReducedMotion(value), t('error.settingsSave'));
  if (data) {
    setState({ reducedMotion: data.reducedMotion });
  }
}

function bindEvents() {
  document.addEventListener('click', (event) => {
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
      changeLanguage(languageBtn.getAttribute('data-language'));
      return;
    }
    const themeBtn = target.closest('[data-theme]');
    if (themeBtn) {
      changeTheme(themeBtn.getAttribute('data-theme'));
      return;
    }
    const motionBtn = target.closest('[data-reduced-motion]');
    if (motionBtn) {
      const next = motionBtn.getAttribute('aria-pressed') !== 'true';
      changeReducedMotion(next);
    }
  });

  document.addEventListener('keydown', (event) => {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      return;
    }
    const items = [...document.querySelectorAll('.nav-item')];
    const index = items.indexOf(document.activeElement);
    if (index < 0) {
      return;
    }
    event.preventDefault();
    const delta = event.key === 'ArrowDown' ? 1 : -1;
    const next = items[(index + delta + items.length) % items.length];
    next.focus();
  });

  window.addEventListener('online', () => setState({ online: true }));
  window.addEventListener('offline', () => setState({ online: false }));

  if (typeof bridge.settings.onChanged === 'function') {
    bridge.settings.onChanged((settings) => {
      if (!settings) {
        return;
      }
      if (settings.language && settings.language !== getState().language) {
        loadLanguage(settings.language);
      }
      setState({
        language: settings.language ?? getState().language,
        theme: settings.theme ?? getState().theme,
        reducedMotion: settings.reducedMotion ?? getState().reducedMotion
      });
    });
  }
}

async function boot() {
  setState({ isElectron: Boolean(bridge.isElectron), online: navigator.onLine });

  const settings = await unwrap(await bridge.settings.get(), t('error.generic'));
  if (settings) {
    loadLanguage(settings.language);
    setState({
      language: settings.language,
      theme: settings.theme,
      reducedMotion: settings.reducedMotion
    });
  } else {
    loadLanguage('fa');
  }

  const info = await unwrap(await bridge.app.getInfo(), t('error.generic'));
  if (info) {
    setState({ appInfo: info });
  }

  subscribe(() => renderShell(getState()));
  bindEvents();
  renderShell(getState());
}

boot().catch((error) => {
  document.body.replaceChildren();
  const pre = document.createElement('pre');
  pre.textContent = error instanceof Error ? error.message : t('error.generic');
  document.body.append(pre);
});
