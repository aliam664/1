/**
 * Versioned schema migrations. Applied in order using PRAGMA user_version.
 * Never edit a published migration — add a new one.
 */
export const MIGRATIONS = Object.freeze([
  Object.freeze({
    version: 1,
    name: 'initial-local-state',
    sql: `
      CREATE TABLE IF NOT EXISTS settings (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS installed_content (
        id TEXT PRIMARY KEY,
        content_type TEXT NOT NULL,
        name TEXT NOT NULL,
        version TEXT NOT NULL,
        install_path TEXT NOT NULL,
        installed_at TEXT NOT NULL,
        source_url TEXT,
        archive_type TEXT,
        checksum TEXT,
        size_bytes INTEGER,
        files_manifest TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS favorites (
        content_id TEXT NOT NULL,
        content_type TEXT NOT NULL,
        added_at TEXT NOT NULL,
        PRIMARY KEY (content_id, content_type)
      );

      CREATE TABLE IF NOT EXISTS download_history (
        id TEXT PRIMARY KEY,
        content_id TEXT,
        url TEXT NOT NULL,
        status TEXT NOT NULL,
        bytes_total INTEGER,
        bytes_done INTEGER,
        started_at TEXT,
        finished_at TEXT,
        error_message TEXT
      );

      CREATE TABLE IF NOT EXISTS download_queue (
        id TEXT PRIMARY KEY,
        content_id TEXT,
        url TEXT NOT NULL,
        priority INTEGER NOT NULL DEFAULT 0,
        state TEXT NOT NULL,
        resume_offset INTEGER NOT NULL DEFAULT 0,
        temp_path TEXT,
        expected_size INTEGER,
        sha256 TEXT,
        file_name TEXT,
        created_at TEXT NOT NULL
      );

      CREATE TABLE IF NOT EXISTS sync_state (
        key TEXT PRIMARY KEY,
        last_sync_at TEXT,
        catalog_version TEXT,
        etag TEXT
      );
    `
  })
]);

/**
 * @param {import('./adapter.js').DatabaseAdapter} db
 * @param {{ info: Function }} logger
 */
export function applyMigrations(db, logger) {
  let current = db.getUserVersion();
  for (const migration of MIGRATIONS) {
    if (migration.version <= current) {
      continue;
    }
    db.transaction(() => {
      db.exec(migration.sql);
      db.setUserVersion(migration.version);
    });
    logger.info('applied database migration', { version: migration.version, name: migration.name });
    current = migration.version;
  }
  return current;
}
