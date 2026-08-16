import { renderCardGrid } from '../components/cards.js';
import { localizedName } from '../services/format.js';
import { getState } from '../state/store.js';

export function renderCatalogPage(type) {
  const query = (getState().search || '').trim().toLowerCase();
  const items = (getState().catalog?.items || []).filter((item) => {
    if (item.type !== type) {
      return false;
    }
    if (!query) {
      return true;
    }
    const hay = `${localizedName(item)} ${item.author} ${item.id} ${(item.tags || []).join(' ')}`.toLowerCase();
    return hay.includes(query);
  });
  const root = document.createElement('div');
  root.innerHTML = renderCardGrid(items);
  return root;
}
