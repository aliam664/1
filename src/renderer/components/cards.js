import { t } from '../i18n/i18n.js';
import { coverHue, formatBytes, isPackaged, localizedName } from '../services/format.js';

export function renderCard(item, options = {}) {
  const hue = Math.floor(coverHue(item.id) / 30);
  const name = localizedName(item);
  const typeKey = `item.type.${item.type}`;
  const packaged = isPackaged(item);
  const badges = [];
  if (item.installed) {
    badges.push(`<span class="badge badge-ok">${t('item.installed')}</span>`);
  }
  if (!packaged) {
    badges.push(`<span class="badge badge-warn">${t('item.sample')}</span>`);
  } else if (!item.sha256) {
    badges.push(`<span class="badge">${t('item.unverified')}</span>`);
  }
  if (item.status === 'deprecated') {
    badges.push(`<span class="badge badge-warn">${t('item.deprecated')}</span>`);
  }
  const primary = item.installed
    ? `<button class="btn" type="button" data-uninstall="${item.id}">${t('item.uninstall')}</button>`
    : `<button class="btn btn-primary" type="button" data-install="${item.id}" ${!packaged || item.status === 'revoked' ? 'disabled' : ''}>
        ${t('item.install')}
      </button>`;
  return `
    <article class="content-card${options.featured ? ' is-featured' : ''}" data-open-item="${item.id}">
      <div class="content-cover hue-${hue}">
        <span class="badge badge-accent cover-chip">${t(typeKey)}</span>
      </div>
      <div class="content-body">
        <h3 class="ltr-isolate">${escapeHtml(name)}</h3>
        <p class="muted ltr-isolate">${escapeHtml(item.author || '')} · ${escapeHtml(item.version || '')}</p>
        <div class="card-meta">
          <span>${formatBytes(item.size)}</span>
          <span class="ltr-isolate">${escapeHtml(String(item.archiveType || '').toUpperCase())}</span>
        </div>
        <div class="card-badges">${badges.join('')}</div>
        <div class="card-actions">
          ${primary}
          <button class="btn btn-quiet" type="button" data-fav="${item.id}" data-fav-type="${item.type}">
            ${item.favorite ? t('item.unfavorite') : t('item.favorite')}
          </button>
        </div>
      </div>
    </article>
  `;
}

export function renderCardGrid(items, options = {}) {
  if (!items.length) {
    return `<article class="card empty-card"><strong data-i18n="home.empty.catalog.title">${t('home.empty.catalog.title')}</strong></article>`;
  }
  const cls = options.featured ? 'card-grid featured-grid' : 'card-grid';
  return `<div class="${cls}">${items.map((item, index) => renderCard(item, { featured: Boolean(options.featured && index === 0) })).join('')}</div>`;
}

export function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
