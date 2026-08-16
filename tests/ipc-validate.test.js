import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  IpcValidationError,
  assertBoolean,
  assertLanguage,
  assertTheme
} from '../src/main/ipc/validate.js';
import { ALLOWED_INVOKE, isAllowedEvent, isAllowedInvoke } from '../src/main/ipc/channels.js';

describe('ipc validators', () => {
  it('accepts the documented language and theme enums', () => {
    assert.equal(assertLanguage('fa'), 'fa');
    assert.equal(assertLanguage('en'), 'en');
    assert.equal(assertTheme('dark'), 'dark');
    assert.equal(assertTheme('light'), 'light');
    assert.equal(assertBoolean(false, 'reducedMotion'), false);
  });

  it('rejects unknown or mistyped values', () => {
    assert.throws(() => assertLanguage('de'), IpcValidationError);
    assert.throws(() => assertLanguage(1), IpcValidationError);
    assert.throws(() => assertTheme('solarized'), IpcValidationError);
    assert.throws(() => assertBoolean('yes', 'reducedMotion'), IpcValidationError);
  });

  it('allow-lists only the Stage 1 invoke channels', () => {
    assert.equal(isAllowedInvoke('app:getInfo'), true);
    assert.equal(isAllowedInvoke('settings:get'), true);
    assert.equal(isAllowedInvoke('fs:read'), false);
    assert.equal(isAllowedInvoke('shell.openExternal'), false);
    assert.equal(isAllowedEvent('settings:changed'), true);
    assert.equal(isAllowedEvent('any'), false);
    assert.equal(ALLOWED_INVOKE.length, 5);
  });
});
