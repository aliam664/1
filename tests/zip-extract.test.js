import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { zipSync } from 'fflate';
import { writeFileSync } from 'node:fs';
import { createArchiveService } from '../src/main/archive/archiveService.js';

function silentLogger() {
  return { info() {}, warn() {}, error() {} };
}

describe('zip extract', () => {
  it('extracts a safe zip and rejects traversal entries', async () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-zip-'));
    const safeZip = path.join(dir, 'safe.zip');
    writeFileSync(
      safeZip,
      zipSync({
        'content/cars/demo/data.acd': new TextEncoder().encode('car')
      })
    );
    const archive = createArchiveService({ logger: silentLogger() });
    const out = path.join(dir, 'out');
    await archive.extract(safeZip, out, { declaredType: 'zip' });
    assert.equal(readFileSync(path.join(out, 'content/cars/demo/data.acd'), 'utf8'), 'car');

    const evilZip = path.join(dir, 'evil.zip');
    writeFileSync(
      evilZip,
      zipSync({
        '../escape.txt': new TextEncoder().encode('nope')
      })
    );
    await assert.rejects(() => archive.extract(evilZip, path.join(dir, 'out2'), { declaredType: 'zip' }));
  });
});
