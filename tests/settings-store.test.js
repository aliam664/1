import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { IpcValidationError } from '../src/main/ipc/validate.js';
import { openDatabase } from '../src/main/database/adapter.js';
import { applyMigrations } from '../src/main/database/migrations.js';
import { createRepositories } from '../src/main/database/repositories.js';
import { createSettingsStore, DEFAULT_SETTINGS } from '../src/main/settings/settingsStore.js';

function silentLogger() {
  return { info() {}, warn() {}, error() {} };
}

async function makeStore() {
  const dir = mkdtempSync(path.join(tmpdir(), 'sc-settings-'));
  const db = await openDatabase(path.join(dir, 'stockcorsa.db'), silentLogger());
  applyMigrations(db, silentLogger());
  const repos = createRepositories(db);
  return { store: createSettingsStore({ repos, logger: silentLogger() }), db };
}

describe('settings store', () => {
  it('defaults to Persian dark theme', async () => {
    const { store, db } = await makeStore();
    const value = store.get();
    assert.equal(value.language, DEFAULT_SETTINGS.language);
    assert.equal(value.theme, DEFAULT_SETTINGS.theme);
    db.close();
  });

  it('persists a valid patch and reloads it', async () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-settings-'));
    const file = path.join(dir, 'stockcorsa.db');
    const db = await openDatabase(file, silentLogger());
    applyMigrations(db, silentLogger());
    const store = createSettingsStore({ repos: createRepositories(db), logger: silentLogger() });
    store.update({ language: 'en', theme: 'light', reducedMotion: true, maxConcurrentDownloads: 3 });
    db.close();

    const db2 = await openDatabase(file, silentLogger());
    applyMigrations(db2, silentLogger());
    const reloaded = createSettingsStore({ repos: createRepositories(db2), logger: silentLogger() });
    const value = reloaded.get();
    assert.equal(value.language, 'en');
    assert.equal(value.theme, 'light');
    assert.equal(value.maxConcurrentDownloads, 3);
    db2.close();
  });

  it('rejects invalid values', async () => {
    const { store, db } = await makeStore();
    assert.throws(() => store.update({ language: 'ar' }), IpcValidationError);
    assert.equal(store.get().language, 'fa');
    db.close();
  });
});
