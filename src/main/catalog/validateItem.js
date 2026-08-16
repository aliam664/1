const ID_PATTERN = /^[a-z0-9]+(?:[._-][a-z0-9]+)*$/;
const SEMVER = /^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/;
const SHA256 = /^[a-fA-F0-9]{64}$/;
const TYPES = new Set(['car', 'track', 'mod']);
const ARCHIVES = new Set(['zip', 'rar', '7z']);
const STATUSES = new Set(['draft', 'published', 'hidden', 'deprecated', 'revoked']);

/**
 * @param {unknown} item
 * @param {string} fileName
 * @returns {{ ok: true, value: object } | { ok: false, error: string }}
 */
export function validateCatalogItem(item, fileName = 'item') {
  if (!item || typeof item !== 'object' || Array.isArray(item)) {
    return fail(fileName, 'item must be an object');
  }
  const rec = /** @type {Record<string, unknown>} */ (item);
  if (typeof rec.id !== 'string' || !ID_PATTERN.test(rec.id) || rec.id.length > 80) {
    return fail(fileName, 'invalid id');
  }
  if (!TYPES.has(rec.type)) {
    return fail(fileName, `invalid type for ${rec.id}`);
  }
  if (!isLocalized(rec.name)) {
    return fail(fileName, `name must have fa and en for ${rec.id}`);
  }
  if (typeof rec.author !== 'string' || rec.author.trim().length === 0 || rec.author.length > 120) {
    return fail(fileName, `invalid author for ${rec.id}`);
  }
  if (!isLocalized(rec.description)) {
    return fail(fileName, `description must have fa and en for ${rec.id}`);
  }
  if (typeof rec.version !== 'string' || !SEMVER.test(rec.version)) {
    return fail(fileName, `invalid version for ${rec.id}`);
  }
  if (!ARCHIVES.has(rec.archiveType)) {
    return fail(fileName, `invalid archiveType for ${rec.id}`);
  }
  if (typeof rec.downloadUrl !== 'string' || !rec.downloadUrl.startsWith('https://')) {
    return fail(fileName, `downloadUrl must be https for ${rec.id}`);
  }
  try {
    const url = new URL(rec.downloadUrl);
    if (url.protocol !== 'https:') {
      return fail(fileName, `downloadUrl must be https for ${rec.id}`);
    }
  } catch {
    return fail(fileName, `downloadUrl is not a valid URL for ${rec.id}`);
  }
  if (rec.sha256 != null && rec.sha256 !== '' && (typeof rec.sha256 !== 'string' || !SHA256.test(rec.sha256))) {
    return fail(fileName, `sha256 must be 64 hex chars for ${rec.id}`);
  }
  if (rec.size != null && (!Number.isInteger(rec.size) || rec.size <= 0)) {
    return fail(fileName, `size must be a positive integer for ${rec.id}`);
  }
  if (rec.status != null && !STATUSES.has(rec.status)) {
    return fail(fileName, `invalid status for ${rec.id}`);
  }
  if (rec.tags != null && (!Array.isArray(rec.tags) || rec.tags.some((tag) => typeof tag !== 'string'))) {
    return fail(fileName, `tags must be a string array for ${rec.id}`);
  }
  if (rec.cover != null && (typeof rec.cover !== 'string' || rec.cover.includes('..') || rec.cover.startsWith('/'))) {
    return fail(fileName, `unsafe cover path for ${rec.id}`);
  }
  return {
    ok: true,
    value: {
      id: rec.id,
      type: rec.type,
      name: rec.name,
      author: rec.author,
      description: rec.description,
      version: rec.version,
      archiveType: rec.archiveType,
      downloadUrl: rec.downloadUrl,
      sha256: rec.sha256 || '',
      size: rec.size || 0,
      status: rec.status || 'published',
      tags: rec.tags || [],
      cover: rec.cover || '',
      featured: Boolean(rec.featured),
      packaged: rec.packaged !== false && !String(rec.downloadUrl).includes('/releases/download/demo/'),
      publishedAt: rec.publishedAt || null,
      updatedAt: rec.updatedAt || rec.publishedAt || null,
      revocationReason: rec.revocationReason || null
    }
  };
}

function isLocalized(value) {
  return Boolean(
    value &&
      typeof value === 'object' &&
      typeof value.fa === 'string' &&
      value.fa.trim() &&
      typeof value.en === 'string' &&
      value.en.trim()
  );
}

function fail(fileName, error) {
  return { ok: false, error: `${fileName}: ${error}` };
}

/**
 * @param {unknown} catalog
 */
export function validateCatalogDocument(catalog) {
  if (!catalog || typeof catalog !== 'object') {
    return { ok: false, error: 'catalog must be an object' };
  }
  const rec = /** @type {Record<string, unknown>} */ (catalog);
  if (!Array.isArray(rec.items)) {
    return { ok: false, error: 'catalog.items must be an array' };
  }
  const ids = new Set();
  const items = [];
  for (const raw of rec.items) {
    const result = validateCatalogItem(raw);
    if (!result.ok) {
      return result;
    }
    if (ids.has(result.value.id)) {
      return { ok: false, error: `duplicate id ${result.value.id}` };
    }
    ids.add(result.value.id);
    items.push(result.value);
  }
  return {
    ok: true,
    value: {
      catalogVersion: String(rec.catalogVersion || '0'),
      generatedAt: rec.generatedAt || null,
      categories: rec.categories || ['car', 'track', 'mod'],
      featured: Array.isArray(rec.featured) ? rec.featured : [],
      items
    }
  };
}
