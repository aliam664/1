import { t } from '../i18n/i18n.js';

/**
 * Honest empty page for capabilities that belong to a later stage.
 * @param {{ titleKey: string, bodyKey: string }} spec
 */
export function renderUnavailable(spec) {
  const root = document.createElement('div');
  root.className = 'page-stack';
  root.innerHTML = `
    <article class="card empty-card">
      <span class="badge badge-warn" data-i18n="page.unavailable.badge">${t('page.unavailable.badge')}</span>
      <strong data-i18n="${spec.titleKey}">${t(spec.titleKey)}</strong>
      <span data-i18n="${spec.bodyKey}">${t(spec.bodyKey)}</span>
    </article>
  `;
  return root;
}

export const UNAVAILABLE_PAGES = Object.freeze({
  cars: { titleKey: 'page.cars.title', bodyKey: 'page.cars.body' },
  tracks: { titleKey: 'page.tracks.title', bodyKey: 'page.tracks.body' },
  mods: { titleKey: 'page.mods.title', bodyKey: 'page.mods.body' },
  favorites: { titleKey: 'page.favorites.title', bodyKey: 'page.favorites.body' },
  downloads: { titleKey: 'page.downloads.title', bodyKey: 'page.downloads.body' },
  updates: { titleKey: 'page.updates.title', bodyKey: 'page.updates.body' }
});
