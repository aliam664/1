import { t } from '../i18n/i18n.js';
import { getState } from '../state/store.js';

export function renderUpdates() {
  const info = getState().updateInfo;
  const items = (getState().catalog?.items || []).filter((item) => item.installed);
  const root = document.createElement('div');
  root.className = 'page-stack';
  root.innerHTML = `
    <section class="card">
      <h2 data-i18n="page.updates.title">${t('page.updates.title')}</h2>
      <div class="meta-list">
        <div class="meta-row">
          <span data-i18n="updates.current">${t('updates.current')}</span>
          <bdi>${getState().appInfo?.version || '1.0.0'}</bdi>
        </div>
      </div>
      <p>${info?.available ? t('updates.available') + ' ' + (info.version || '') : t('updates.none')}</p>
      <div class="card-actions">
        <button class="btn btn-primary" type="button" data-action="check-updates">${t('updates.check')}</button>
        ${info?.url ? `<button class="btn" type="button" data-open-url="${info.url}">${t('updates.open')}</button>` : ''}
      </div>
    </section>
    <section class="card">
      <h2 data-i18n="updates.content">${t('updates.content')}</h2>
      <p class="muted">${items.length ? items.map((item) => item.id).join(' · ') : t('home.empty.installed.body')}</p>
    </section>
  `;
  return root;
}
