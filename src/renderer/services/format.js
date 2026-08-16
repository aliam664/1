import { getLanguage } from '../i18n/i18n.js';

export function localizedName(item) {
  const lang = getLanguage();
  if (!item?.name) {
    return item?.id || '';
  }
  return item.name[lang] || item.name.en || item.name.fa || item.id;
}

export function localizedDescription(item) {
  const lang = getLanguage();
  if (!item?.description) {
    return '';
  }
  return item.description[lang] || item.description.en || item.description.fa || '';
}

export function formatBytes(bytes) {
  const n = Number(bytes) || 0;
  if (n < 1024) {
    return `${n} B`;
  }
  if (n < 1024 * 1024) {
    return `${(n / 1024).toFixed(1)} KB`;
  }
  if (n < 1024 * 1024 * 1024) {
    return `${(n / (1024 * 1024)).toFixed(1)} MB`;
  }
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function formatSpeed(bytesPerSec) {
  if (!bytesPerSec) {
    return '—';
  }
  return `${formatBytes(bytesPerSec)}/s`;
}

export function formatEta(seconds) {
  if (seconds == null || !Number.isFinite(seconds)) {
    return '—';
  }
  const s = Math.max(0, Math.round(seconds));
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}`;
}

export function coverHue(id) {
  let hash = 0;
  for (const ch of String(id || '')) {
    hash = (hash + ch.charCodeAt(0) * 17) % 360;
  }
  return hash;
}

/**
 * Sample catalog rows that point at unpublished /demo/ assets must not
 * pretend they can be installed.
 */
export function isPackaged(item) {
  if (!item) {
    return false;
  }
  if (item.packaged === false) {
    return false;
  }
  return !String(item.downloadUrl || '').includes('/releases/download/demo/');
}
