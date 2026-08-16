import { APP_CONFIG } from '../config/appConfig.js';
import { UPDATER_ENDPOINTS } from '../config/endpoints.js';
import { AppError, ErrorCodes } from '../app/errors.js';

/**
 * Compare dotted semver-ish strings. Returns 1 if a > b.
 * @param {string} a
 * @param {string} b
 */
export function compareVersions(a, b) {
  const pa = String(a).split('.').map((n) => Number.parseInt(n, 10) || 0);
  const pb = String(b).split('.').map((n) => Number.parseInt(n, 10) || 0);
  const len = Math.max(pa.length, pb.length);
  for (let i = 0; i < len; i += 1) {
    const da = pa[i] || 0;
    const db = pb[i] || 0;
    if (da > db) {
      return 1;
    }
    if (da < db) {
      return -1;
    }
  }
  return 0;
}

/**
 * @param {{ http: { request: Function }, channel?: string }} options
 */
export function createUpdater(options) {
  async function check() {
    const url =
      options.channel === 'beta' ? UPDATER_ENDPOINTS.releasesApi : UPDATER_ENDPOINTS.latestReleaseApi;
    const response = await options.http.request(url, {
      headers: { Accept: 'application/vnd.github+json' }
    });
    const body = await response.json();
    const release = Array.isArray(body) ? pickNewest(body, options.channel) : body;
    if (!release || release.draft) {
      throw new AppError(ErrorCodes.UPDATE_NONE, 'No release information');
    }
    const remote = String(release.tag_name || release.name || '').replace(/^v/, '');
    const newer = compareVersions(remote, APP_CONFIG.version) > 0;
    return {
      current: APP_CONFIG.version,
      available: newer,
      version: remote,
      notes: release.body || '',
      url: release.html_url,
      publishedAt: release.published_at,
      prerelease: Boolean(release.prerelease)
    };
  }

  return { check };
}

function pickNewest(releases, channel) {
  const usable = releases.filter((item) => !item.draft && (channel === 'beta' || !item.prerelease));
  return usable[0] || null;
}
