import { createWriteStream, mkdirSync, readdirSync, renameSync, statSync } from 'node:fs';
import path from 'node:path';

const LEVELS = Object.freeze({ debug: 10, info: 20, warn: 30, error: 40 });
const MAX_FILE_BYTES = 2 * 1024 * 1024;
const MAX_FILES = 5;
const SECRET_KEYS = /token|password|secret|authorization|api[_-]?key|cookie/i;

/**
 * @param {unknown} value
 * @returns {unknown}
 */
function redact(value) {
  if (value == null) {
    return value;
  }
  if (typeof value === 'string') {
    return value;
  }
  if (Array.isArray(value)) {
    return value.map(redact);
  }
  if (typeof value === 'object') {
    /** @type {Record<string, unknown>} */
    const copy = {};
    for (const [key, nested] of Object.entries(value)) {
      copy[key] = SECRET_KEYS.test(key) ? '[redacted]' : redact(nested);
    }
    return copy;
  }
  return value;
}

/**
 * @param {string} logsDir
 * @param {{ level?: keyof typeof LEVELS }} [options]
 */
export function createLogger(logsDir, options = {}) {
  mkdirSync(logsDir, { recursive: true });
  const minLevel = LEVELS[options.level || 'info'] ?? LEVELS.info;
  const filePath = path.join(logsDir, 'stockcorsa.log');
  let stream = createWriteStream(filePath, { flags: 'a' });

  function rotateIfNeeded() {
    try {
      const size = statSync(filePath).size;
      if (size < MAX_FILE_BYTES) {
        return;
      }
      stream.end();
      const stamp = new Date().toISOString().replace(/[:.]/g, '-');
      renameSync(filePath, path.join(logsDir, `stockcorsa-${stamp}.log`));
      const extras = readdirSync(logsDir)
        .filter((name) => name.startsWith('stockcorsa-') && name.endsWith('.log'))
        .sort();
      while (extras.length >= MAX_FILES) {
        const oldest = extras.shift();
        if (oldest) {
          try {
            renameSync(path.join(logsDir, oldest), path.join(logsDir, `${oldest}.old`));
          } catch {
            // ignore rotation races
          }
        }
      }
      stream = createWriteStream(filePath, { flags: 'a' });
    } catch {
      // A failed rotation must never crash the app.
    }
  }

  /**
   * @param {keyof typeof LEVELS} level
   * @param {string} message
   * @param {Record<string, unknown>} [fields]
   */
  function write(level, message, fields) {
    if (LEVELS[level] < minLevel) {
      return;
    }
    rotateIfNeeded();
    const line = JSON.stringify({
      ts: new Date().toISOString(),
      level,
      message,
      ...(fields ? { fields: redact(fields) } : {})
    });
    stream.write(`${line}\n`);
    const printer = level === 'error' ? console.error : level === 'warn' ? console.warn : console.log;
    printer(`[${level}] ${message}`);
  }

  return {
    debug(message, fields) {
      write('debug', message, fields);
    },
    info(message, fields) {
      write('info', message, fields);
    },
    warn(message, fields) {
      write('warn', message, fields);
    },
    error(message, fields) {
      write('error', message, fields);
    },
    child(extra) {
      return {
        debug(message, fields) {
          write('debug', message, { ...extra, ...fields });
        },
        info(message, fields) {
          write('info', message, { ...extra, ...fields });
        },
        warn(message, fields) {
          write('warn', message, { ...extra, ...fields });
        },
        error(message, fields) {
          write('error', message, { ...extra, ...fields });
        }
      };
    },
    close() {
      return new Promise((resolve) => {
        stream.end(resolve);
      });
    }
  };
}
