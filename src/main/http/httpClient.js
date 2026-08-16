import { APP_CONFIG } from '../config/appConfig.js';
import { isTrustedHttpsUrl } from '../config/endpoints.js';
import { AppError, ErrorCodes } from '../app/errors.js';

/**
 * @param {number} attempt
 * @param {number} retryAfterMs
 */
export function backoffDelay(attempt, retryAfterMs = 0) {
  if (retryAfterMs > 0) {
    return Math.min(retryAfterMs, 30_000);
  }
  const base = Math.min(1000 * 2 ** attempt, 16_000);
  const jitter = Math.floor(Math.random() * 250);
  return base + jitter;
}

/**
 * @param {{
 *   fetchImpl?: typeof fetch,
 *   logger: { info: Function, warn: Function, error: Function },
 *   userAgent?: string,
 *   maxAttempts?: number,
 *   timeoutMs?: number,
 *   allowUnlistedHttps?: boolean
 * }} options
 */
export function createHttpClient(options) {
  const fetchImpl = options.fetchImpl || globalThis.fetch;
  const maxAttempts = options.maxAttempts || APP_CONFIG.catalog.maxAttempts;
  const timeoutMs = options.timeoutMs || APP_CONFIG.catalog.metadataTimeoutMs;

  /**
   * @param {string} url
   * @param {{ method?: string, headers?: Record<string, string>, signal?: AbortSignal }} [init]
   */
  async function request(url, init = {}) {
    if (!url.startsWith('https://')) {
      throw new AppError(ErrorCodes.HOST_UNTRUSTED, 'Only HTTPS URLs are allowed');
    }
    if (!options.allowUnlistedHttps && !isTrustedHttpsUrl(url)) {
      throw new AppError(ErrorCodes.HOST_UNTRUSTED, 'Host is not on the trusted list');
    }
    let lastError = null;
    for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
      const controller = new AbortController();
      const timer = setTimeout(() => controller.abort(), timeoutMs);
      try {
        const response = await fetchImpl(url, {
          method: init.method || 'GET',
          headers: {
            'User-Agent': options.userAgent || APP_CONFIG.userAgent,
            Accept: 'application/json,application/octet-stream,*/*',
            ...(init.headers || {})
          },
          redirect: 'manual',
          signal: init.signal || controller.signal
        });
        clearTimeout(timer);
        if (response.status === 304) {
          return response;
        }
        if (response.status === 429 || response.status === 403) {
          const retryAfter = Number(response.headers.get('Retry-After')) * 1000;
          options.logger.warn('rate limited', { url, status: response.status, attempt });
          if (attempt === maxAttempts - 1) {
            throw new AppError(ErrorCodes.RATE_LIMIT, 'Remote host rate-limited the request');
          }
          await sleep(backoffDelay(attempt, Number.isFinite(retryAfter) ? retryAfter : 0));
          continue;
        }
        if (response.status >= 500) {
          options.logger.warn('server error', { url, status: response.status, attempt });
          lastError = new AppError(ErrorCodes.NETWORK, `Server error ${response.status}`);
          await sleep(backoffDelay(attempt));
          continue;
        }
        if (response.status >= 400) {
          throw new AppError(ErrorCodes.NETWORK, `Request failed with ${response.status}`);
        }
        return response;
      } catch (error) {
        clearTimeout(timer);
        if (error instanceof AppError) {
          throw error;
        }
        const err = /** @type {Error & { code?: string, name?: string }} */ (error);
        const code = err.code || err.name;
        options.logger.warn('http request failed', { url, code, message: err.message, attempt });
        lastError = new AppError(
          code === 'AbortError' ? ErrorCodes.TIMEOUT : ErrorCodes.NETWORK,
          err.message || 'Network error'
        );
        if (attempt === maxAttempts - 1) {
          throw lastError;
        }
        await sleep(backoffDelay(attempt));
      }
    }
    throw lastError || new AppError(ErrorCodes.NETWORK, 'Request failed');
  }

  return { request };
}

function sleep(ms) {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}
