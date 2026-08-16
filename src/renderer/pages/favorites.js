import { t } from '../i18n/i18n.js';
import { renderCardGrid } from '../components/cards.js';
import { getState } from '../state/store.js';

export function renderFavorites() {
  const items = (getState().catalog?.items || []).filter((item) => item.favorite);
  const root = document.createElement('div');
  root.innerHTML = items.length
    ? renderCardGrid(items)
    : `<article class="card empty-card"><strong data-i18n="page.favorites.empty">${t('page.favorites.empty')}</strong></article>`;
  return root;
}
