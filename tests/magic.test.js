import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { detectMagic, isMultiVolumeName, MAGIC } from '../src/main/archive/magic.js';

describe('archive magic', () => {
  it('detects zip rar4 rar5 and 7z', () => {
    assert.equal(detectMagic(MAGIC.zip), 'zip');
    assert.equal(detectMagic(MAGIC.rar4), 'rar');
    assert.equal(detectMagic(MAGIC.rar5), 'rar');
    assert.equal(detectMagic(MAGIC.sevenZ), '7z');
    assert.equal(detectMagic(Buffer.from([0, 1, 2, 3])), 'unknown');
  });

  it('flags multi-volume names', () => {
    assert.equal(isMultiVolumeName('mod.part1.rar'), true);
    assert.equal(isMultiVolumeName('mod.r00'), true);
    assert.equal(isMultiVolumeName('mod.rar'), false);
  });
});
