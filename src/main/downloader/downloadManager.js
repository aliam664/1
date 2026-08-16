import { createWriteStream, existsSync, mkdirSync, renameSync, rmSync, statSync } from 'node:fs';
import path from 'node:path';
import { APP_CONFIG } from '../config/appConfig.js';
import { AppError, ErrorCodes } from '../app/errors.js';
import { sha256File } from './checksum.js';

/**
 * @param {{
 *   repos: ReturnType<import('../database/repositories.js').createRepositories>,
 *   tempDir: string,
 *   logger: { info: Function, warn: Function, error: Function },
 *   fetchImpl?: typeof fetch,
 *   onProgress?: (payload: object) => void,
 *   getConcurrency?: () => number
 * }} options
 */
export function createDownloadManager(options) {
  const { repos, tempDir, logger, onProgress } = options;
  const fetchImpl = options.fetchImpl || globalThis.fetch;
  mkdirSync(tempDir, { recursive: true });

  /** @type {Map<string, { controller: AbortController, paused: boolean }>} */
  const live = new Map();
  /** @type {Map<string, { samples: { t: number, bytes: number }[], lastEmit: number }>} */
  const meters = new Map();
  let pumping = false;

  function emit(payload) {
    onProgress?.(payload);
  }

  function snapshot(id, extra = {}) {
    const row = repos.queue.get(id);
    if (!row) {
      return extra;
    }
    return {
      id: row.id,
      contentId: row.content_id,
      url: row.url,
      state: row.state,
      bytesDone: row.resume_offset,
      bytesTotal: row.expected_size,
      fileName: row.file_name,
      ...extra
    };
  }

  async function probeResume(url) {
    try {
      const response = await fetchImpl(url, {
        method: 'GET',
        headers: { Range: 'bytes=0-0', 'User-Agent': APP_CONFIG.userAgent },
        redirect: 'manual'
      });
      if (response.body) {
        try {
          await response.arrayBuffer();
        } catch {
          // ignore unread-body errors; we only needed headers
        }
      }
      const accept = response.headers.get('Accept-Ranges') || '';
      const contentRange = response.headers.get('Content-Range') || '';
      return response.status === 206 || accept.includes('bytes') || contentRange.startsWith('bytes');
    } catch {
      return false;
    }
  }

  async function enqueue(item) {
    const existing = repos.queue.list().find((row) => row.content_id === item.contentId && row.state !== 'completed');
    if (existing) {
      return existing.id;
    }
    const id = repos.queue.insert({
      contentId: item.contentId,
      url: item.url,
      state: 'queued',
      expectedSize: item.size || null,
      sha256: item.sha256 || '',
      fileName: item.fileName || `${item.contentId}.part`,
      priority: item.priority || 0
    });
    repos.history.add({
      id: `${id}-hist`,
      contentId: item.contentId,
      url: item.url,
      status: 'queued',
      bytesTotal: item.size || null,
      bytesDone: 0
    });
    pump();
    return id;
  }

  function pause(id) {
    const handle = live.get(id);
    const row = repos.queue.get(id);
    if (!row) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'Download not found');
    }
    if (row.state === 'running' && handle) {
      handle.paused = true;
      handle.controller.abort();
    }
    repos.queue.update(id, { state: 'paused' });
    emit(snapshot(id, { state: 'paused' }));
  }

  function resume(id) {
    const row = repos.queue.get(id);
    if (!row) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'Download not found');
    }
    repos.queue.update(id, { state: 'queued' });
    emit(snapshot(id, { state: 'queued' }));
    pump();
  }

  function cancel(id) {
    const handle = live.get(id);
    handle?.controller.abort();
    const row = repos.queue.get(id);
    if (row?.temp_path && existsSync(row.temp_path)) {
      rmSync(row.temp_path, { force: true });
    }
    repos.queue.remove(id);
    live.delete(id);
    emit({ id, state: 'canceled', contentId: row?.content_id });
  }

  function retry(id) {
    const row = repos.queue.get(id);
    if (!row) {
      throw new AppError(ErrorCodes.NOT_FOUND, 'Download not found');
    }
    if (row.temp_path && existsSync(row.temp_path)) {
      rmSync(row.temp_path, { force: true });
    }
    repos.queue.update(id, { state: 'queued', resumeOffset: 0 });
    pump();
  }

  async function pump() {
    if (pumping) {
      return;
    }
    pumping = true;
    try {
      const concurrency = options.getConcurrency?.() || APP_CONFIG.download.defaultConcurrency;
      while (true) {
        const running = repos.queue.list().filter((row) => row.state === 'running').length;
        const next = repos.queue.list().find((row) => row.state === 'queued');
        if (!next || running >= concurrency) {
          break;
        }
        void run(next.id);
        await Promise.resolve();
      }
    } finally {
      pumping = false;
    }
  }

  async function run(id) {
    const row = repos.queue.get(id);
    if (!row) {
      return;
    }
    if (!row.url.startsWith('https://')) {
      fail(id, new AppError(ErrorCodes.HOST_UNTRUSTED, 'Downloads must be HTTPS'));
      return;
    }
    const controller = new AbortController();
    live.set(id, { controller, paused: false });
    const tempPath = row.temp_path || path.join(tempDir, `${id}.part`);
    mkdirSync(path.dirname(tempPath), { recursive: true });
    const existing = existsSync(tempPath) ? statSync(tempPath).size : 0;
    const canResume = existing > 0 ? await probeResume(row.url) : await probeResume(row.url);
    if (existing > 0 && !canResume) {
      rmSync(tempPath, { force: true });
    }
    const offset = canResume && existing > 0 ? existing : 0;
    repos.queue.update(id, { state: 'running', tempPath, resumeOffset: offset, expectedSize: row.expected_size });
    emit(snapshot(id, { state: 'running', resumable: canResume }));

    const headers = { 'User-Agent': APP_CONFIG.userAgent };
    if (offset > 0) {
      headers.Range = `bytes=${offset}-`;
    }
    try {
      const response = await fetchHttps(row.url, { headers, signal: controller.signal, fetchImpl });
      if (!response.ok && response.status !== 206) {
        throw new AppError(ErrorCodes.NETWORK, `Download failed (${response.status})`);
      }
      const contentType = response.headers.get('content-type') || '';
      if (contentType.includes('text/html')) {
        throw new AppError(ErrorCodes.DOWNLOAD_HTML, 'Host returned a web page instead of a file');
      }
      const totalHeader = Number(response.headers.get('content-length')) || 0;
      const total = response.status === 206 ? offset + totalHeader : totalHeader || row.expected_size || 0;
      if (row.expected_size && total && Math.abs(total - row.expected_size) / row.expected_size > 0.25) {
        logger.warn('content-length differs from catalog size', { id, total, expected: row.expected_size });
      }
      const stream = response.body;
      if (!stream) {
        throw new AppError(ErrorCodes.NETWORK, 'Empty download body');
      }
      const file = createWriteStream(tempPath, { flags: offset > 0 ? 'a' : 'w' });
      let done = offset;
      meters.set(id, { samples: [{ t: Date.now(), bytes: done }], lastEmit: 0 });
      const reader = stream.getReader();
      try {
        while (true) {
          const { value, done: finished } = await reader.read();
          if (finished) {
            break;
          }
          if (!value) {
            continue;
          }
          done += value.byteLength;
          await new Promise((resolve, reject) => {
            file.write(Buffer.from(value), (error) => (error ? reject(error) : resolve()));
          });
          maybeProgress(id, done, total, canResume);
        }
      } finally {
        await new Promise((resolve) => file.end(resolve));
      }
      repos.queue.update(id, { state: 'verifying', resumeOffset: done, expectedSize: total, tempPath });
      let checksumOk = true;
      let digest = '';
      if (row.sha256) {
        digest = await sha256File(tempPath);
        checksumOk = digest.toLowerCase() === String(row.sha256).toLowerCase();
        if (!checksumOk) {
          rmSync(tempPath, { force: true });
          throw new AppError(ErrorCodes.CHECKSUM, 'Download verification failed');
        }
      }
      const finalPath = tempPath.replace(/\.part$/, '') || `${tempPath}.bin`;
      renameSync(tempPath, finalPath);
      repos.queue.update(id, { state: 'completed', resumeOffset: done, tempPath: finalPath });
      repos.history.add({
        contentId: row.content_id,
        url: row.url,
        status: 'completed',
        bytesTotal: total,
        bytesDone: done,
        finishedAt: new Date().toISOString()
      });
      live.delete(id);
      emit(snapshot(id, { state: 'completed', filePath: finalPath, checksum: digest, verified: Boolean(row.sha256) }));
    } catch (error) {
      const handle = live.get(id);
      live.delete(id);
      if (handle?.paused) {
        repos.queue.update(id, { state: 'paused' });
        emit(snapshot(id, { state: 'paused', resumable: true }));
      } else {
        fail(id, error);
      }
    } finally {
      pump();
    }
  }

  function maybeProgress(id, done, total, resumable) {
    const meter = meters.get(id) || { samples: [], lastEmit: 0 };
    const t = Date.now();
    meter.samples.push({ t, bytes: done });
    meter.samples = meter.samples.filter((sample) => t - sample.t < 4000);
    if (t - meter.lastEmit < APP_CONFIG.download.progressThrottleMs) {
      meters.set(id, meter);
      return;
    }
    meter.lastEmit = t;
    meters.set(id, meter);
    const first = meter.samples[0];
    const dt = (t - first.t) / 1000;
    const speed = dt > 0 ? (done - first.bytes) / dt : 0;
    const remaining = total > done && speed > 0 ? (total - done) / speed : null;
    repos.queue.update(id, { resumeOffset: done, expectedSize: total || null });
    emit(snapshot(id, { state: 'running', bytesDone: done, bytesTotal: total, speed, eta: remaining, resumable }));
  }

  function fail(id, error) {
    const message = error instanceof Error ? error.message : String(error);
    const code = error instanceof AppError ? error.code : ErrorCodes.NETWORK;
    repos.queue.update(id, { state: 'failed' });
    const row = repos.queue.get(id);
    repos.history.add({
      contentId: row?.content_id,
      url: row?.url || '',
      status: 'failed',
      errorMessage: message,
      finishedAt: new Date().toISOString()
    });
    logger.error('download failed', { id, code, message });
    emit(snapshot(id, { state: 'failed', error: { code, message } }));
  }

  function list() {
    return repos.queue.list().map((row) => ({
      id: row.id,
      contentId: row.content_id,
      url: row.url,
      state: row.state,
      bytesDone: row.resume_offset,
      bytesTotal: row.expected_size,
      fileName: row.file_name,
      filePath: row.state === 'completed' ? row.temp_path : null,
      resumable: row.state === 'paused'
    }));
  }

  function restore() {
    for (const row of repos.queue.list()) {
      if (row.state === 'running') {
        repos.queue.update(row.id, { state: 'queued' });
      }
    }
    pump();
  }

  return { enqueue, pause, resume, cancel, retry, list, restore, probeResume };
}

/**
 * Follow redirects only while the hop stays on HTTPS.
 * @param {string} url
 * @param {{ headers: Record<string, string>, signal: AbortSignal, fetchImpl: typeof fetch }} options
 */
async function fetchHttps(url, options) {
  let current = url;
  for (let hop = 0; hop < APP_CONFIG.catalog.maximumRedirects; hop += 1) {
    if (!current.startsWith('https://')) {
      throw new AppError(ErrorCodes.HOST_UNTRUSTED, 'Redirect left HTTPS');
    }
    const response = await options.fetchImpl(current, {
      headers: options.headers,
      signal: options.signal,
      redirect: 'manual'
    });
    if ([301, 302, 303, 307, 308].includes(response.status)) {
      const location = response.headers.get('location');
      if (!location) {
        throw new AppError(ErrorCodes.NETWORK, 'Redirect without Location');
      }
      current = new URL(location, current).toString();
      continue;
    }
    return response;
  }
  throw new AppError(ErrorCodes.NETWORK, 'Too many redirects');
}
