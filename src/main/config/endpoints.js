/**
 * Single source of truth for every remote URL the launcher may contact.
 * Change host / branch / path here — never inside catalog, download or
 * updater logic.
 *
 * Stage 1 only *defines* these values. Network I/O starts in Stage 3
 * (catalog) and Stage 8 (updater).
 */

export const GITHUB_OWNER = 'aliam664';
export const GITHUB_REPO = '1';
export const CATALOG_BRANCH = 'catalog';
export const UPDATE_REPO = `${GITHUB_OWNER}/${GITHUB_REPO}`;

const JSDELIVR_BASE = `https://cdn.jsdelivr.net/gh/${GITHUB_OWNER}/${GITHUB_REPO}@${CATALOG_BRANCH}`;
const RAW_BASE = `https://raw.githubusercontent.com/${GITHUB_OWNER}/${GITHUB_REPO}/${CATALOG_BRANCH}`;

/**
 * Ordered catalog delivery chain. The client walks this list and stops
 * at the first successful, schema-valid response.
 *
 * @type {ReadonlyArray<{ id: string, manifestUrl: string, catalogUrl: string }>}
 */
export const CATALOG_SOURCES = Object.freeze([
  Object.freeze({
    id: 'jsdelivr',
    manifestUrl: `${JSDELIVR_BASE}/dist/manifest.json`,
    catalogUrl: `${JSDELIVR_BASE}/dist/catalog.json`
  }),
  Object.freeze({
    id: 'raw-github',
    manifestUrl: `${RAW_BASE}/dist/manifest.json`,
    catalogUrl: `${RAW_BASE}/dist/catalog.json`
  })
]);

export const UPDATER_ENDPOINTS = Object.freeze({
  releasesApi: `https://api.github.com/repos/${UPDATE_REPO}/releases`,
  latestReleaseApi: `https://api.github.com/repos/${UPDATE_REPO}/releases/latest`
});

/**
 * Hosts the Main process is allowed to contact. Renderer has connect-src
 * 'none' and never sees these.
 */
export const TRUSTED_HOSTS = Object.freeze([
  'cdn.jsdelivr.net',
  'raw.githubusercontent.com',
  'github.com',
  'api.github.com',
  'objects.githubusercontent.com',
  'aliam664.github.io'
]);

/**
 * @param {string} url
 * @returns {boolean}
 */
export function isTrustedHttpsUrl(url) {
  let parsed;
  try {
    parsed = new URL(url);
  } catch {
    return false;
  }
  if (parsed.protocol !== 'https:') {
    return false;
  }
  return TRUSTED_HOSTS.includes(parsed.hostname);
}

/**
 * Every URL this module exports. Used by tests to guarantee nothing
 * accidentally points at http:// or an unknown host.
 *
 * @returns {string[]}
 */
export function listDeclaredUrls() {
  const urls = [];
  for (const source of CATALOG_SOURCES) {
    urls.push(source.manifestUrl, source.catalogUrl);
  }
  urls.push(UPDATER_ENDPOINTS.releasesApi, UPDATER_ENDPOINTS.latestReleaseApi);
  return urls;
}
