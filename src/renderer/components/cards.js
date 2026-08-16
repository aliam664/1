import { t } from '../i18n/i18n.js';
import { coverHue, formatBytes, localizedName } from '../services/format.js';

export function renderCard(item) {
  const hue = coverHue(item.id);
  const name = localizedName(item);
  const badges = [];
  if (item.installed) {
    badges.push(`<span class="badge">${t('item.installed')}</span>`);
  }
  if (!item.sha256) {
    badges.push(`<span class="badge">${t('item.unverified')}</span>`);
  }
  if (item.status === 'deprecated') {
    badges.push(`<span class="badge badge-warn">${t('item.deprecated')}</span>`);
  }
  return `
    <article class="content-card" data-open-item="${item.id}">
      <div class="content-cover hue-${hue}"></div>
      <div class="content-body">
        <h3 class="ltr-isolate">${escapeHtml(name)}</h3>
        <p class="muted ltr-isolate">${escapeHtml(item.author || '')} · ${escapeHtml(item.version || '')}</p>
        <div class="card-meta">
          <span>${formatBytes(item.size)}</span>
          <span class="ltr-isolate">${escapeHtml(String(item.archiveType || '').toUpperCase())}</span>
        </div>
        <div class="card-badges">${badges.join('')}</div>
        <div class="card-actions">
          <button class="btn btn-primary" type="button" data-install="${item.id}" ${item.installed || item.status === 'revoked' ? 'disabled' : ''}>
            ${item.installed ? t('item.installed') : t('item.install')}
          </button>
          <button class="btn btn-quiet" type="button" data-fav="${item.id}" data-fav-type="${item.type}">
            ${item.favorite ? t('item.unfavorite') : t('item.favorite')}
          </button>
        </div>
      </div>
    </article>
  `;
}

export function renderCardGrid(items) {
  if (!items.length) {
    return `<article class="card empty-card"><strong data-i18n="home.empty.catalog.title">${t('home.empty.catalog.title')}</strong></article>`;
  }
  return `<div class="card-grid">${items.map(renderCard).join('')}</div>`;
}

export function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
