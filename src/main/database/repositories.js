import { randomUUID } from 'node:crypto';

/**
 * @param {import('./adapter.js').DatabaseAdapter} db
 */
export function createRepositories(db) {
  const settingsGet = db.prepare('SELECT value FROM settings WHERE key = ?');
  const settingsAll = db.prepare('SELECT key, value FROM settings');
  const settingsUpsert = db.prepare(
    'INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value'
  );

  const installedGet = db.prepare('SELECT * FROM installed_content WHERE id = ?');
  const installedList = db.prepare('SELECT * FROM installed_content ORDER BY installed_at DESC');
  const installedUpsert = db.prepare(`
    INSERT INTO installed_content (
      id, content_type, name, version, install_path, installed_at,
      source_url, archive_type, checksum, size_bytes, files_manifest
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(id) DO UPDATE SET
      content_type = excluded.content_type,
      name = excluded.name,
      version = excluded.version,
      install_path = excluded.install_path,
      installed_at = excluded.installed_at,
      source_url = excluded.source_url,
      archive_type = excluded.archive_type,
      checksum = excluded.checksum,
      size_bytes = excluded.size_bytes,
      files_manifest = excluded.files_manifest
  `);
  const installedDelete = db.prepare('DELETE FROM installed_content WHERE id = ?');

  const favList = db.prepare('SELECT * FROM favorites ORDER BY added_at DESC');
  const favGet = db.prepare('SELECT * FROM favorites WHERE content_id = ? AND content_type = ?');
  const favAdd = db.prepare(
    'INSERT INTO favorites (content_id, content_type, added_at) VALUES (?, ?, ?)'
  );
  const favRemove = db.prepare('DELETE FROM favorites WHERE content_id = ? AND content_type = ?');

  const queueList = db.prepare('SELECT * FROM download_queue ORDER BY priority DESC, created_at ASC');
  const queueGet = db.prepare('SELECT * FROM download_queue WHERE id = ?');
  const queueInsert = db.prepare(`
    INSERT INTO download_queue (
      id, content_id, url, priority, state, resume_offset, temp_path,
      expected_size, sha256, file_name, created_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  const queueUpdate = db.prepare(`
    UPDATE download_queue
    SET state = ?, resume_offset = ?, temp_path = ?, expected_size = ?
    WHERE id = ?
  `);
  const queueDelete = db.prepare('DELETE FROM download_queue WHERE id = ?');

  const historyInsert = db.prepare(`
    INSERT INTO download_history (
      id, content_id, url, status, bytes_total, bytes_done, started_at, finished_at, error_message
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  const historyList = db.prepare('SELECT * FROM download_history ORDER BY started_at DESC LIMIT 100');

  const syncGet = db.prepare('SELECT * FROM sync_state WHERE key = ?');
  const syncUpsert = db.prepare(`
    INSERT INTO sync_state (key, last_sync_at, catalog_version, etag)
    VALUES (?, ?, ?, ?)
    ON CONFLICT(key) DO UPDATE SET
      last_sync_at = excluded.last_sync_at,
      catalog_version = excluded.catalog_version,
      etag = excluded.etag
  `);

  return {
    settings: {
      getAll() {
        /** @type {Record<string, string>} */
        const out = {};
        for (const row of settingsAll.all()) {
          out[row.key] = row.value;
        }
        return out;
      },
      get(key) {
        const row = settingsGet.get(key);
        return row ? row.value : undefined;
      },
      set(key, value) {
        settingsUpsert.run(key, String(value));
      }
    },
    installed: {
      get(id) {
        const row = installedGet.get(id);
        return row ? decodeInstalled(row) : null;
      },
      list() {
        return installedList.all().map(decodeInstalled);
      },
      upsert(record) {
        installedUpsert.run(
          record.id,
          record.contentType,
          record.name,
          record.version,
          record.installPath,
          record.installedAt,
          record.sourceUrl || null,
          record.archiveType || null,
          record.checksum || null,
          record.sizeBytes ?? null,
          JSON.stringify(record.filesManifest || [])
        );
      },
      remove(id) {
        installedDelete.run(id);
      }
    },
    favorites: {
      list() {
        return favList.all();
      },
      has(contentId, contentType) {
        return Boolean(favGet.get(contentId, contentType));
      },
      toggle(contentId, contentType) {
        if (favGet.get(contentId, contentType)) {
          favRemove.run(contentId, contentType);
          return false;
        }
        favAdd.run(contentId, contentType, new Date().toISOString());
        return true;
      }
    },
    queue: {
      list() {
        return queueList.all();
      },
      get(id) {
        return queueGet.get(id) || null;
      },
      insert(record) {
        const id = record.id || randomUUID();
        queueInsert.run(
          id,
          record.contentId || null,
          record.url,
          record.priority ?? 0,
          record.state,
          record.resumeOffset ?? 0,
          record.tempPath || null,
          record.expectedSize ?? null,
          record.sha256 || null,
          record.fileName || null,
          record.createdAt || new Date().toISOString()
        );
        return id;
      },
      update(id, patch) {
        const current = queueGet.get(id);
        if (!current) {
          return;
        }
        queueUpdate.run(
          patch.state ?? current.state,
          patch.resumeOffset ?? current.resume_offset,
          patch.tempPath ?? current.temp_path,
          patch.expectedSize ?? current.expected_size,
          id
        );
      },
      remove(id) {
        queueDelete.run(id);
      }
    },
    history: {
      add(record) {
        historyInsert.run(
          record.id || randomUUID(),
          record.contentId || null,
          record.url,
          record.status,
          record.bytesTotal ?? null,
          record.bytesDone ?? null,
          record.startedAt || new Date().toISOString(),
          record.finishedAt || null,
          record.errorMessage || null
        );
      },
      list() {
        return historyList.all();
      }
    },
    sync: {
      get(key = 'catalog') {
        return syncGet.get(key) || null;
      },
      set(record) {
        syncUpsert.run(
          record.key || 'catalog',
          record.lastSyncAt || new Date().toISOString(),
          record.catalogVersion || null,
          record.etag || null
        );
      }
    }
  };
}

function decodeInstalled(row) {
  let filesManifest = [];
  try {
    filesManifest = JSON.parse(row.files_manifest || '[]');
  } catch {
    filesManifest = [];
  }
  return {
    id: row.id,
    contentType: row.content_type,
    name: row.name,
    version: row.version,
    installPath: row.install_path,
    installedAt: row.installed_at,
    sourceUrl: row.source_url,
    archiveType: row.archive_type,
    checksum: row.checksum,
    sizeBytes: row.size_bytes,
    filesManifest
  };
}
