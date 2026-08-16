import { createRequire } from 'node:module';
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import initSqlJs from 'sql.js';

const require = createRequire(import.meta.url);

/**
 * @typedef {object} Statement
 * @property {(...params: unknown[]) => unknown} get
 * @property {(...params: unknown[]) => unknown[]} all
 * @property {(...params: unknown[]) => { changes: number }} run
 */

/**
 * @typedef {object} DatabaseAdapter
 * @property {'better-sqlite3' | 'sql.js'} engine
 * @property {(sql: string) => void} exec
 * @property {(sql: string) => Statement} prepare
 * @property {() => number} getUserVersion
 * @property {(version: number) => void} setUserVersion
 * @property {(fn: () => unknown) => unknown} transaction
 * @property {() => void} persist
 * @property {() => void} close
 */

function wrapBetterSqlite(raw, logger) {
  raw.pragma('journal_mode = WAL');
  raw.pragma('foreign_keys = ON');
  logger.info('database opened', { engine: 'better-sqlite3' });
  return {
    engine: 'better-sqlite3',
    exec(sql) {
      raw.exec(sql);
    },
    prepare(sql) {
      const stmt = raw.prepare(sql);
      return {
        get: (...params) => stmt.get(...params),
        all: (...params) => stmt.all(...params),
        run: (...params) => {
          const info = stmt.run(...params);
          return { changes: info.changes };
        }
      };
    },
    getUserVersion() {
      return Number(raw.pragma('user_version', { simple: true })) || 0;
    },
    setUserVersion(version) {
      raw.pragma(`user_version = ${Number(version)}`);
    },
    transaction(fn) {
      return raw.transaction(fn)();
    },
    persist() {},
    close() {
      raw.close();
    }
  };
}

async function openSqlJs(filePath, logger) {
  const wasmPath = path.join(path.dirname(require.resolve('sql.js')), 'sql-wasm.wasm');
  const SQL = await initSqlJs({ wasmBinary: readFileSync(wasmPath) });
  const raw = existsSync(filePath) ? new SQL.Database(readFileSync(filePath)) : new SQL.Database();
  raw.run('PRAGMA foreign_keys = ON');

  function persist() {
    mkdirSync(path.dirname(filePath), { recursive: true });
    const tmp = `${filePath}.tmp`;
    writeFileSync(tmp, Buffer.from(raw.export()));
    renameSync(tmp, filePath);
  }

  function bindAndStep(sql, params) {
    const stmt = raw.prepare(sql);
    if (params.length) {
      stmt.bind(params);
    }
    return stmt;
  }

  logger.info('database opened', { engine: 'sql.js', filePath });

  return {
    engine: 'sql.js',
    exec(sql) {
      raw.exec(sql);
    },
    prepare(sql) {
      return {
        get(...params) {
          const stmt = bindAndStep(sql, params);
          const row = stmt.step() ? stmt.getAsObject() : undefined;
          stmt.free();
          return row;
        },
        all(...params) {
          const stmt = bindAndStep(sql, params);
          const rows = [];
          while (stmt.step()) {
            rows.push(stmt.getAsObject());
          }
          stmt.free();
          return rows;
        },
        run(...params) {
          const stmt = bindAndStep(sql, params);
          while (stmt.step()) {
            /* consume */
          }
          stmt.free();
          persist();
          return { changes: raw.getRowsModified() };
        }
      };
    },
    getUserVersion() {
      const result = raw.exec('PRAGMA user_version');
      return Number(result?.[0]?.values?.[0]?.[0]) || 0;
    },
    setUserVersion(version) {
      raw.run(`PRAGMA user_version = ${Number(version)}`);
    },
    transaction(fn) {
      const value = fn();
      persist();
      return value;
    },
    persist,
    close() {
      persist();
      raw.close();
    }
  };
}

/**
 * Prefer better-sqlite3 (native, sync). Fall back to sql.js which still
 * reads/writes a real SQLite file, so switching engines later is a no-op
 * for callers and for on-disk data.
 *
 * @param {string} filePath
 * @param {{ info: Function, warn: Function, error: Function }} logger
 * @returns {Promise<DatabaseAdapter>}
 */
export async function openDatabase(filePath, logger) {
  mkdirSync(path.dirname(filePath), { recursive: true });
  try {
    const Database = require('better-sqlite3');
    return wrapBetterSqlite(new Database(filePath), logger);
  } catch (error) {
    logger.warn('better-sqlite3 unavailable, using sql.js adapter', {
      message: error instanceof Error ? error.message : String(error)
    });
    return openSqlJs(filePath, logger);
  }
}
