import assert from 'node:assert/strict';
import { mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { AppError } from '../src/main/app/errors.js';
import { createInstaller } from '../src/main/installer/installer.js';

describe('installer analyze', () => {
  it('refuses to extract when the game path is missing', async () => {
    const installer = createInstaller({
      repos: { installed: { list() { return []; } } },
      archive: { extract() { throw new Error('should not extract'); } },
      tempDir: mkdtempSync(path.join(tmpdir(), 'sc-inst-')),
      logger: { info() {}, warn() {}, error() {} },
      getGamePath: () => ''
    });
    await assert.rejects(() => installer.analyze('/tmp/mod.zip', 'zip'), (error) => {
      assert.equal(error instanceof AppError, true);
      assert.equal(error.code, 'GAME_PATH_INVALID');
      return true;
    });
  });
});
