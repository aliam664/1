import { t } from '../i18n/i18n.js';
import { heroMark } from '../components/icons.js';

function emptyCard(titleKey, bodyKey) {
  return `
    <article class="card empty-card">
      <strong data-i18n="${titleKey}">${t(titleKey)}</strong>
      <span data-i18n="${bodyKey}">${t(bodyKey)}</span>
    </article>
  `;
}

export function renderHome() {
  const root = document.createElement('div');
  root.className = 'page-home';
  root.innerHTML = `
    <section class="hero">
      <div>
        <p class="badge" data-i18n="home.greeting">${t('home.greeting')}</p>
        <h2 data-i18n="app.tagline">${t('app.tagline')}</h2>
        <p data-i18n="home.subtitle">${t('home.subtitle')}</p>
        <button class="btn btn-primary" type="button" data-nav="settings" data-i18n="home.cta.settings">
          ${t('home.cta.settings')}
        </button>
      </div>
      ${heroMark}
    </section>

    <section class="section">
      <div class="section-head">
        <h2 data-i18n="home.featured">${t('home.featured')}</h2>
      </div>
      ${emptyCard('home.empty.catalog.title', 'home.empty.catalog.body')}
    </section>

    <section class="section">
      <div class="section-head">
        <h2 data-i18n="home.recentlyAdded">${t('home.recentlyAdded')}</h2>
      </div>
      <div class="card-grid">
        ${emptyCard('home.empty.catalog.title', 'home.empty.catalog.body')}
        ${emptyCard('home.empty.catalog.title', 'home.empty.catalog.body')}
      </div>
    </section>

    <section class="section">
      <div class="section-head">
        <h2 data-i18n="home.recentlyUpdated">${t('home.recentlyUpdated')}</h2>
      </div>
      ${emptyCard('home.empty.catalog.title', 'home.empty.catalog.body')}
    </section>

    <section class="section">
      <div class="section-head">
        <h2 data-i18n="home.popular">${t('home.popular')}</h2>
      </div>
      ${emptyCard('home.empty.catalog.title', 'home.empty.catalog.body')}
    </section>

    <section class="section">
      <div class="section-head">
        <h2 data-i18n="home.installed">${t('home.installed')}</h2>
      </div>
      ${emptyCard('home.empty.installed.title', 'home.empty.installed.body')}
    </section>
  `;
  return root;
}
