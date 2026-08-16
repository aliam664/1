import assert from 'node:assert/strict';
import path from 'node:path';
import { describe, it } from 'node:test';
import { underRoot } from '../src/main/installer/installer.js';

describe('install path containment', () => {
  it('does not treat target-evil as inside target', () => {
    const root = path.resolve('/tmp/assetto/content/cars/demo');
    assert.equal(underRoot(root, path.join(root, 'data.acd')), true);
    assert.equal(underRoot(root, `${root}-evil/data.acd`), false);
    assert.equal(underRoot(root, path.resolve(root, '..', 'other')), false);
  });
});
