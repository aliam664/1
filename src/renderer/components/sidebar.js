import { t } from '../i18n/i18n.js';
import { icons } from './icons.js';

export const NAV_ITEMS = Object.freeze([
  Object.freeze({
    group: 'nav.group.discover',
    items: [
      { id: 'home', icon: 'home', labelKey: 'nav.home' },
      { id: 'cars', icon: 'cars', labelKey: 'nav.cars' },
      { id: 'tracks', icon: 'tracks', labelKey: 'nav.tracks' },
      { id: 'mods', icon: 'mods', labelKey: 'nav.mods' }
    ]
  }),
  Object.freeze({
    group: 'nav.group.library',
    items: [
      { id: 'favorites', icon: 'favorites', labelKey: 'nav.favorites' },
      { id: 'downloads', icon: 'downloads', labelKey: 'nav.downloads', badge: 'downloads' },
      { id: 'updates', icon: 'updates', labelKey: 'nav.updates', badge: 'updates' }
    ]
  }),
  Object.freeze({
    group: 'nav.group.system',
    items: [{ id: 'settings', icon: 'settings', labelKey: 'nav.settings' }]
  })
]);

export const ALL_ROUTES = NAV_ITEMS.flatMap((group) => group.items.map((item) => item.id));

/**
 * @param {{ route: string, online: boolean, isElectron: boolean, downloadCount?: number, updateCount?: number }} model
 */
export function renderSidebar(model) {
  const groups = NAV_ITEMS.map((group) => {
    const items = group.items
      .map((item) => {
        const current = item.id === model.route;
        const count = item.badge === 'downloads' ? model.downloadCount : item.badge === 'updates' ? model.updateCount : 0;
        const badge = count
          ? `<span class="nav-badge">${count}</span>`
          : '<span class="nav-badge" hidden>0</span>';
        return `
          <button
            class="nav-item"
            type="button"
            data-nav="${item.id}"
            aria-current="${current ? 'page' : 'false'}"
          >
            ${icons[item.icon]}
            <span data-i18n="${item.labelKey}">${t(item.labelKey)}</span>
            ${badge}
          </button>
        `;
      })
      .join('');
    return `
      <div class="nav-group">
        <div class="nav-label" data-i18n="${group.group}">${t(group.group)}</div>
        ${items}
      </div>
    `;
  }).join('');

  const statusKey = !model.isElectron ? 'status.preview' : model.online ? 'status.online' : 'status.offline';

  return `
    <div class="brand">
      <svg class="brand-mark" viewBox="0 0 36 36" aria-hidden="true" focusable="false">
        <rect width="36" height="36" rx="10" fill="#de4058"/>
        <path d="M8 23c6-9 14-9 20 0" fill="none" stroke="#fff" stroke-width="2.4" stroke-linecap="round"/>
        <circle cx="24" cy="13" r="3" fill="#fff"/>
      </svg>
      <div class="brand-copy">
        <div class="brand-name ltr-isolate">StockCorsa</div>
        <div class="brand-tagline" data-i18n="app.tagline">${t('app.tagline')}</div>
      </div>
    </div>
    <nav class="nav-groups" aria-label="${t('a11y.sidebar')}" data-i18n-aria="a11y.sidebar">
      ${groups}
    </nav>
    <div class="sidebar-status" data-online="${model.online ? 'true' : 'false'}" data-i18n-aria="a11y.statusbar">
      <span class="status-dot"></span>
      <span data-i18n="${statusKey}">${t(statusKey)}</span>
    </div>
  `;
}
