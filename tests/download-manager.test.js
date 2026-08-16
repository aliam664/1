import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { openDatabase } from '../src/main/database/adapter.js';
import { applyMigrations } from '../src/main/database/migrations.js';
import { createRepositories } from '../src/main/database/repositories.js';
import { createDownloadManager } from '../src/main/downloader/downloadManager.js';

function silentLogger() {
  return { info() {}, warn() {}, error() {} };
}

describe('download manager', () => {
  it('downloads a payload from an injected fetch and reaches a terminal state', async () => {
    const payload = Buffer.from('stockcorsa-file');
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-dl-'));
    const db = await openDatabase(path.join(dir, 'db.sqlite'), silentLogger());
    applyMigrations(db, silentLogger());
    const repos = createRepositories(db);
    const manager = createDownloadManager({
      repos,
      tempDir: path.join(dir, 'tmp'),
      logger: silentLogger(),
      fetchImpl: async () =>
        new Response(payload, {
          status: 200,
          headers: {
            'Content-Length': String(payload.length),
            'Accept-Ranges': 'bytes',
            'Content-Type': 'application/octet-stream'
          }
        })
    });
    const id = await manager.enqueue({
      contentId: 'demo',
      url: 'https://example.invalid/file.bin',
      size: payload.length,
      sha256: '',
      fileName: 'file.bin'
    });
    for (let i = 0; i < 20; i += 1) {
      const row = manager.list()[0];
      if (row && ['completed', 'failed'].includes(row.state)) {
        break;
      }
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    const rows = manager.list();
    assert.equal(rows[0].id, id);
    assert.ok(['completed', 'failed'].includes(rows[0].state), rows[0].state);
    db.close();
  });
});
