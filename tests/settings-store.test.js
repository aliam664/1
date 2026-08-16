import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { IpcValidationError } from '../src/main/ipc/validate.js';
import { createSettingsStore, DEFAULT_SETTINGS } from '../src/main/settings/settingsStore.js';

function silentLogger() {
  return { info() {}, warn() {}, error() {} };
}

describe('settings store', () => {
  it('defaults to Persian dark theme', () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-settings-'));
    const store = createSettingsStore({
      filePath: path.join(dir, 'settings.json'),
      logger: silentLogger()
    });
    assert.deepEqual(store.get(), DEFAULT_SETTINGS);
  });

  it('persists a valid patch and reloads it', () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-settings-'));
    const filePath = path.join(dir, 'settings.json');
    const store = createSettingsStore({ filePath, logger: silentLogger() });
    store.update({ language: 'en', theme: 'light', reducedMotion: true });

    const disk = JSON.parse(readFileSync(filePath, 'utf8'));
    assert.equal(disk.language, 'en');
    assert.equal(disk.theme, 'light');

    const reloaded = createSettingsStore({ filePath, logger: silentLogger() });
    assert.deepEqual(reloaded.get(), {
      language: 'en',
      theme: 'light',
      reducedMotion: true
    });
  });

  it('rejects invalid values and leaves the previous snapshot intact', () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-settings-'));
    const store = createSettingsStore({
      filePath: path.join(dir, 'settings.json'),
      logger: silentLogger()
    });
    assert.throws(() => store.update({ language: 'ar' }), IpcValidationError);
    assert.deepEqual(store.get(), DEFAULT_SETTINGS);
  });
});
