import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import {
  CONTENT_SECURITY_POLICY,
  WEB_PREFERENCES,
  assertStrictCsp
} from '../src/main/app/securityPolicy.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

describe('security policy', () => {
  it('enables isolation, sandbox and webSecurity and disables Node in the renderer', () => {
    assert.equal(WEB_PREFERENCES.contextIsolation, true);
    assert.equal(WEB_PREFERENCES.nodeIntegration, false);
    assert.equal(WEB_PREFERENCES.nodeIntegrationInWorker, false);
    assert.equal(WEB_PREFERENCES.sandbox, true);
    assert.equal(WEB_PREFERENCES.webSecurity, true);
    assert.equal(WEB_PREFERENCES.webviewTag, false);
    assert.equal(WEB_PREFERENCES.allowRunningInsecureContent, false);
  });

  it('uses a strict CSP without unsafe-inline or unsafe-eval', () => {
    const result = assertStrictCsp(CONTENT_SECURITY_POLICY);
    assert.equal(result.ok, true, result.reason);
  });

  it('keeps the HTML meta CSP identical to the session policy', () => {
    const html = readFileSync(path.join(ROOT, 'src/renderer/index.html'), 'utf8');
    const match = html.match(/http-equiv="Content-Security-Policy"\s+content="([^"]+)"/);
    assert.ok(match, 'index.html must declare a CSP meta tag');
    const normalizedMeta = match[1].replace(/;\s+/g, '; ');
    const normalizedPolicy = CONTENT_SECURITY_POLICY.replace(/;\s+/g, '; ');
    assert.equal(normalizedMeta, normalizedPolicy);
  });

  it('does not load any CDN or remote origin from the renderer HTML', () => {
    const html = readFileSync(path.join(ROOT, 'src/renderer/index.html'), 'utf8');
    assert.equal(/https?:\/\//i.test(html), false);
  });

  it('uses a CommonJS sandboxed preload', () => {
    const preload = readFileSync(path.join(ROOT, 'src/preload/index.cjs'), 'utf8');
    assert.match(preload, /contextBridge\.exposeInMainWorld\('stockcorsa'/);
    assert.doesNotMatch(preload, /exposeInMainWorld\('ipcRenderer'/);
    assert.doesNotMatch(preload, /exposeInMainWorld\([^,]+,\s*ipcRenderer/);
  });
});
