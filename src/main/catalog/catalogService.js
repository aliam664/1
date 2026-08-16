import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { APP_CONFIG } from '../config/appConfig.js';
import { CATALOG_SOURCES } from '../config/endpoints.js';
import { AppError, ErrorCodes } from '../app/errors.js';
import { validateCatalogDocument } from './validateItem.js';

/**
 * @param {{
 *   cacheDir: string,
 *   http: { request: Function },
 *   repos: ReturnType<import('../database/repositories.js').createRepositories>,
 *   logger: { info: Function, warn: Function, error: Function },
 *   now?: () => number,
 *   overrideManifestUrl?: string
 * }} options
 */
export function createCatalogService(options) {
  const { cacheDir, http, repos, logger } = options;
  const now = options.now || (() => Date.now());
  mkdirSync(cacheDir, { recursive: true });
  const catalogFile = path.join(cacheDir, 'catalog.json');
  const manifestFile = path.join(cacheDir, 'manifest.json');

  /** @type {object | null} */
  let memory = loadCache();

  function loadCache() {
    if (!existsSync(catalogFile)) {
      return null;
    }
    try {
      const parsed = JSON.parse(readFileSync(catalogFile, 'utf8'));
      const valid = validateCatalogDocument(parsed);
      return valid.ok ? valid.value : null;
    } catch {
      return null;
    }
  }

  function persist(doc, manifest) {
    const tmp = `${catalogFile}.tmp`;
    writeFileSync(tmp, `${JSON.stringify(doc)}\n`);
    renameSync(tmp, catalogFile);
    if (manifest) {
      writeFileSync(manifestFile, `${JSON.stringify(manifest)}\n`);
    }
    memory = doc;
  }

  function visibleItems() {
    const items = memory?.items || [];
    return items.filter((item) => item.status === 'published' || item.status === 'deprecated');
  }

  async function fetchJson(url, headers = {}) {
    const response = await http.request(url, { headers });
    if (response.status === 304) {
      return { status: 304, etag: response.headers.get('ETag'), json: null };
    }
    const text = await response.text();
    if (text.length > APP_CONFIG.catalog.maximumBytes) {
      throw new AppError(ErrorCodes.NETWORK, 'Catalog payload is too large');
    }
    return {
      status: response.status,
      etag: response.headers.get('ETag'),
      json: JSON.parse(text)
    };
  }

  async function sync(force = false) {
    const state = repos.sync.get('catalog');
    if (!force && state?.last_sync_at) {
      const elapsed = now() - Date.parse(state.last_sync_at);
      if (Number.isFinite(elapsed) && elapsed < APP_CONFIG.catalog.minSyncIntervalMs) {
        return { status: 'throttled', catalog: getSnapshot() };
      }
    }

    const sources = options.overrideManifestUrl
      ? [{ id: 'override', manifestUrl: options.overrideManifestUrl, catalogUrl: options.overrideManifestUrl.replace(/manifest\.json$/, 'catalog.json') }]
      : CATALOG_SOURCES;

    let lastError = null;
    for (const source of sources) {
      try {
        const headers = {};
        if (state?.etag) {
          headers['If-None-Match'] = state.etag;
        }
        const manifestRes = await fetchJson(source.manifestUrl, headers);
        if (manifestRes.status === 304 && memory) {
          repos.sync.set({
            key: 'catalog',
            lastSyncAt: new Date(now()).toISOString(),
            catalogVersion: state?.catalog_version,
            etag: state?.etag
          });
          return { status: 'not-modified', catalog: getSnapshot(), source: source.id };
        }
        const manifest = manifestRes.json;
        const sameVersion =
          manifest &&
          state?.catalog_version &&
          (manifest.catalogVersion === state.catalog_version || manifest.sha256 === undefined);
        if (sameVersion && memory && manifest.sha256) {
          const local = existsSync(catalogFile) ? shaOfFile(catalogFile) : '';
          if (local && local === manifest.sha256) {
            repos.sync.set({
              key: 'catalog',
              lastSyncAt: new Date(now()).toISOString(),
              catalogVersion: manifest.catalogVersion,
              etag: manifestRes.etag
            });
            return { status: 'not-modified', catalog: getSnapshot(), source: source.id };
          }
        }
        const catalogRes = await fetchJson(source.catalogUrl);
        const valid = validateCatalogDocument(catalogRes.json);
        if (!valid.ok) {
          throw new AppError(ErrorCodes.NETWORK, valid.error);
        }
        if (valid.value.items.length > APP_CONFIG.catalog.maximumItems) {
          throw new AppError(ErrorCodes.NETWORK, 'Catalog has too many items');
        }
        persist(valid.value, manifest);
        repos.sync.set({
          key: 'catalog',
          lastSyncAt: new Date(now()).toISOString(),
          catalogVersion: valid.value.catalogVersion,
          etag: manifestRes.etag || catalogRes.etag
        });
        logger.info('catalog synced', { source: source.id, items: valid.value.items.length });
        return { status: 'updated', catalog: getSnapshot(), source: source.id };
      } catch (error) {
        lastError = error;
        logger.warn('catalog source failed', {
          source: source.id,
          message: error instanceof Error ? error.message : String(error)
        });
      }
    }

    if (memory) {
      logger.warn('catalog sync fell back to local cache');
      return { status: 'cache', catalog: getSnapshot(), error: lastError?.message };
    }
    throw lastError || new AppError(ErrorCodes.OFFLINE, 'Catalog is unavailable');
  }

  function getSnapshot() {
    const installed = new Set(options.repos.installed.list().map((row) => row.id));
    const favorites = new Set(options.repos.favorites.list().map((row) => row.content_id));
    const items = visibleItems().map((item) => ({
      ...item,
      installed: installed.has(item.id),
      favorite: favorites.has(item.id),
      verified: Boolean(item.sha256)
    }));
    return {
      catalogVersion: memory?.catalogVersion || null,
      generatedAt: memory?.generatedAt || null,
      featured: memory?.featured || [],
      categories: memory?.categories || ['car', 'track', 'mod'],
      items,
      itemCount: items.length,
      offline: !memory
    };
  }

  function getItem(id) {
    return getSnapshot().items.find((item) => item.id === id) || null;
  }

  function seed(doc) {
    const valid = validateCatalogDocument(doc);
    if (!valid.ok) {
      throw new AppError(ErrorCodes.INTERNAL, valid.error);
    }
    persist(valid.value, null);
  }

  return { sync, getSnapshot, getItem, seed, hasCache: () => Boolean(memory) };
}

function shaOfFile(filePath) {
  return createHash('sha256').update(readFileSync(filePath)).digest('hex');
}
