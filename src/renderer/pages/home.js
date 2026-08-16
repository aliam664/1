import { t } from '../i18n/i18n.js';
import { heroMark } from '../components/icons.js';
import { renderCardGrid } from '../components/cards.js';
import { getState } from '../state/store.js';

function pick(items, ids) {
  const set = new Set(ids || []);
  const featured = items.filter((item) => set.has(item.id) || item.featured);
  return featured.length ? featured : items.slice(0, 3);
}

export function renderHome() {
  const { catalog } = getState();
  const items = catalog?.items || [];
  const featured = pick(items, catalog?.featured);
  const recent = [...items].sort((a, b) => String(b.publishedAt).localeCompare(String(a.publishedAt))).slice(0, 4);
  const updated = [...items].sort((a, b) => String(b.updatedAt).localeCompare(String(a.updatedAt))).slice(0, 4);
  const popular = items.slice(0, 4);
  const installed = items.filter((item) => item.installed);

  const root = document.createElement('div');
  root.className = 'page-home';
  root.innerHTML = `
    <section class="hero">
      <div>
        <p class="badge" data-i18n="home.greeting">${t('home.greeting')}</p>
        <h2 data-i18n="app.tagline">${t('app.tagline')}</h2>
        <p data-i18n="home.subtitle">${t('home.subtitle')}</p>
        <button class="btn btn-primary" type="button" data-action="refresh-catalog" data-i18n="home.cta.refresh">${t('home.cta.refresh')}</button>
      </div>
      ${heroMark}
    </section>
    <section class="section">
      <div class="section-head"><h2 data-i18n="home.featured">${t('home.featured')}</h2></div>
      ${renderCardGrid(featured)}
    </section>
    <section class="section">
      <div class="section-head"><h2 data-i18n="home.recentlyAdded">${t('home.recentlyAdded')}</h2></div>
      ${renderCardGrid(recent)}
    </section>
    <section class="section">
      <div class="section-head"><h2 data-i18n="home.recentlyUpdated">${t('home.recentlyUpdated')}</h2></div>
      ${renderCardGrid(updated)}
    </section>
    <section class="section">
      <div class="section-head"><h2 data-i18n="home.popular">${t('home.popular')}</h2></div>
      ${renderCardGrid(popular)}
    </section>
    <section class="section">
      <div class="section-head"><h2 data-i18n="home.installed">${t('home.installed')}</h2></div>
      ${installed.length ? renderCardGrid(installed) : `<article class="card empty-card"><strong data-i18n="home.empty.installed.title">${t('home.empty.installed.title')}</strong><span data-i18n="home.empty.installed.body">${t('home.empty.installed.body')}</span></article>`}
    </section>
  `;
  return root;
}
