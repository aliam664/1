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

function sourcePair(id, base) {
  return Object.freeze({
    id,
    manifestUrl: `${base}/dist/manifest.json`,
    catalogUrl: `${base}/dist/catalog.json`
  });
}

/**
 * Ordered catalog delivery chain. The client walks this list and stops
 * at the first successful, schema-valid response.
 *
 * `catalog` is the published aggregation branch. `main` is the fallback
 * after the PR lands so the launcher still works before that branch exists.
 */
export const CATALOG_SOURCES = Object.freeze([
  sourcePair(
    'jsdelivr',
    `https://cdn.jsdelivr.net/gh/${GITHUB_OWNER}/${GITHUB_REPO}@${CATALOG_BRANCH}`
  ),
  sourcePair(
    'raw-github',
    `https://raw.githubusercontent.com/${GITHUB_OWNER}/${GITHUB_REPO}/${CATALOG_BRANCH}`
  ),
  sourcePair(
    'jsdelivr-main',
    `https://cdn.jsdelivr.net/gh/${GITHUB_OWNER}/${GITHUB_REPO}@main`
  ),
  sourcePair(
    'raw-github-main',
    `https://raw.githubusercontent.com/${GITHUB_OWNER}/${GITHUB_REPO}/main`
  )
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
