import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { resolveSafeDestination } from '../src/main/filesystem/safePath.js';

const TARGET = path.resolve('/tmp/stockcorsa-target');

describe('safe path', () => {
  it('accepts a normal relative entry', () => {
    const dest = resolveSafeDestination('content/cars/bmw/data.acd', TARGET);
    assert.equal(dest.startsWith(TARGET), true);
  });

  it('rejects zip-slip, absolute and reserved names', () => {
    assert.throws(() => resolveSafeDestination('../evil.txt', TARGET));
    assert.throws(() => resolveSafeDestination('..\\evil.txt', TARGET));
    assert.throws(() => resolveSafeDestination('C:/Windows/notepad.exe', TARGET));
    assert.throws(() => resolveSafeDestination('/etc/passwd', TARGET));
    assert.throws(() => resolveSafeDestination('cars/CON/data.acd', TARGET));
    assert.throws(() => resolveSafeDestination('ok/file.txt\0.jpg', TARGET));
    assert.throws(() => resolveSafeDestination('ok/file:ads.txt', TARGET));
  });
});
