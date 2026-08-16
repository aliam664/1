import { t } from '../i18n/i18n.js';
import { formatBytes, formatEta, formatSpeed, localizedName } from '../services/format.js';
import { getState } from '../state/store.js';
import { escapeHtml } from '../components/cards.js';

export function renderDownloads() {
  const rows = getState().downloads || [];
  const root = document.createElement('div');
  if (!rows.length) {
    root.innerHTML = `<article class="card empty-card"><strong data-i18n="page.downloads.empty">${t('page.downloads.empty')}</strong></article>`;
    return root;
  }
  root.innerHTML = rows
    .map((row) => {
      const pct = row.bytesTotal ? Math.round((row.bytesDone / row.bytesTotal) * 100) : 0;
      const bucket = Math.min(100, Math.round(pct / 10) * 10);
      const item = (getState().catalog?.items || []).find((entry) => entry.id === row.contentId);
      const title = item ? localizedName(item) : row.contentId || row.id;
      const canPause = row.state === 'running' && row.resumable !== false;
      return `
        <article class="card download-row">
          <div class="section-head">
            <h2 class="ltr-isolate">${escapeHtml(title)}</h2>
            <span class="badge">${t(`download.${row.state}`) || row.state}</span>
          </div>
          <div class="progress p${bucket}"><span></span></div>
          <p class="muted ltr-isolate">${formatBytes(row.bytesDone)} / ${formatBytes(row.bytesTotal)} · ${formatSpeed(row.speed)} · ${formatEta(row.eta)}</p>
          ${row.resumable === false ? `<p class="muted">${t('download.noResume')}</p>` : ''}
          <div class="card-actions">
            ${canPause ? `<button class="btn" type="button" data-dl-pause="${row.id}">${t('download.pause')}</button>` : ''}
            ${row.state === 'paused' ? `<button class="btn" type="button" data-dl-resume="${row.id}">${t('download.resume')}</button>` : ''}
            ${row.state === 'failed' ? `<button class="btn" type="button" data-dl-retry="${row.id}">${t('download.retry')}</button>` : ''}
            <button class="btn" type="button" data-dl-cancel="${row.id}">${t('download.cancel')}</button>
          </div>
        </article>
      `;
    })
    .join('');
  return root;
}
