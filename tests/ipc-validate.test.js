import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  IpcValidationError,
  assertBoolean,
  assertHttpsUrl,
  assertId,
  assertLanguage,
  assertTheme
} from '../src/main/ipc/validate.js';
import { ALLOWED_INVOKE, isAllowedEvent, isAllowedInvoke } from '../src/main/ipc/channels.js';

describe('ipc validators', () => {
  it('accepts the documented language and theme enums', () => {
    assert.equal(assertLanguage('fa'), 'fa');
    assert.equal(assertTheme('dark'), 'dark');
    assert.equal(assertBoolean(false, 'reducedMotion'), false);
    assert.equal(assertId('bmw-m3-e46'), 'bmw-m3-e46');
    assert.equal(assertHttpsUrl('https://example.com/a.zip'), 'https://example.com/a.zip');
  });

  it('rejects unknown or mistyped values', () => {
    assert.throws(() => assertLanguage('de'), IpcValidationError);
    assert.throws(() => assertTheme('solarized'), IpcValidationError);
    assert.throws(() => assertBoolean('yes', 'reducedMotion'), IpcValidationError);
    assert.throws(() => assertHttpsUrl('http://evil.example/x'), IpcValidationError);
    assert.throws(() => assertId('../etc/passwd'), IpcValidationError);
  });

  it('allow-lists invoke channels and rejects fs/shell smuggling', () => {
    assert.equal(isAllowedInvoke('app:getInfo'), true);
    assert.equal(isAllowedInvoke('catalog:sync'), true);
    assert.equal(isAllowedInvoke('downloads:enqueue'), true);
    assert.equal(isAllowedInvoke('fs:read'), false);
    assert.equal(isAllowedEvent('download:progress'), true);
    assert.equal(ALLOWED_INVOKE.length >= 20, true);
  });
});
