import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const VERSION_FILE = path.resolve(HERE, '../../../launcher/version.json');

function readVersion() {
  const raw = readFileSync(VERSION_FILE, 'utf8');
  const parsed = JSON.parse(raw);
  if (typeof parsed.version !== 'string' || !/^\d+\.\d+\.\d+/.test(parsed.version)) {
    throw new Error('launcher/version.json is missing a valid version');
  }
  return parsed;
}

const versionInfo = readVersion();

/**
 * Immutable runtime configuration. No secrets live here.
 */
export const APP_CONFIG = Object.freeze({
  appId: 'com.stockcorsa.launcher',
  productName: 'StockCorsa Launcher',
  version: versionInfo.version,
  channel: versionInfo.channel || 'stable',
  userAgent: `StockCorsaLauncher/${versionInfo.version}`,
  catalog: Object.freeze({
    maximumBytes: 4 * 1024 * 1024,
    maximumItems: 4000,
    metadataTimeoutMs: 20_000,
    maximumRedirects: 5,
    minSyncIntervalMs: 15 * 60 * 1000,
    maxAttempts: 4
  }),
  download: Object.freeze({
    defaultConcurrency: 2,
    maxConcurrency: 4,
    progressThrottleMs: 250,
    stallTimeoutMs: 30_000,
    connectTimeoutMs: 20_000
  }),
  archive: Object.freeze({
    maxExtractedBytes: 8 * 1024 * 1024 * 1024,
    maxCompressionRatio: 100,
    maxEntries: 20_000
  })
});
