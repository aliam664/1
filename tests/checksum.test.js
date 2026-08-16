import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { sha256Buffer, sha256File } from '../src/main/downloader/checksum.js';

describe('checksum', () => {
  it('hashes a file without loading caller-side buffers', async () => {
    const dir = mkdtempSync(path.join(tmpdir(), 'sc-hash-'));
    const file = path.join(dir, 'a.bin');
    writeFileSync(file, 'hello-stockcorsa');
    const digest = await sha256File(file);
    assert.equal(digest, sha256Buffer(Buffer.from('hello-stockcorsa')));
    assert.equal(digest.length, 64);
  });
});
