import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { inspectGamePath } from '../src/main/assetto-corsa/validatePath.js';

describe('assetto corsa path', () => {
  it('requires exe plus content/cars and content/tracks', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'sc-ac-'));
    assert.equal(inspectGamePath(root).valid, false);
    writeFileSync(path.join(root, 'AssettoCorsa.exe'), '');
    mkdirSync(path.join(root, 'content', 'cars'), { recursive: true });
    mkdirSync(path.join(root, 'content', 'tracks'), { recursive: true });
    const inspected = inspectGamePath(root);
    assert.equal(inspected.valid, true);
    assert.equal(inspected.missing.includes('executable'), false);
  });
});
