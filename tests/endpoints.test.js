import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  CATALOG_SOURCES,
  isTrustedHttpsUrl,
  listDeclaredUrls,
  TRUSTED_HOSTS
} from '../src/main/config/endpoints.js';

describe('endpoints', () => {
  it('declares only https URLs on trusted hosts', () => {
    const urls = listDeclaredUrls();
    assert.equal(urls.length > 0, true);
    for (const url of urls) {
      assert.equal(isTrustedHttpsUrl(url), true, url);
      assert.equal(url.startsWith('https://'), true, url);
    }
  });

  it('lists jsDelivr first, then raw GitHub, then main-branch fallbacks', () => {
    assert.equal(CATALOG_SOURCES[0].id, 'jsdelivr');
    assert.equal(CATALOG_SOURCES[1].id, 'raw-github');
    assert.equal(CATALOG_SOURCES[2].id, 'jsdelivr-main');
    assert.equal(CATALOG_SOURCES[3].id, 'raw-github-main');
    assert.match(CATALOG_SOURCES[0].manifestUrl, /jsdelivr/);
    assert.match(CATALOG_SOURCES[1].manifestUrl, /raw\.githubusercontent\.com/);
    assert.match(CATALOG_SOURCES[3].manifestUrl, /\/main\/dist\/manifest\.json$/);
  });

  it('rejects http and unknown hosts', () => {
    assert.equal(isTrustedHttpsUrl('http://cdn.jsdelivr.net/x'), false);
    assert.equal(isTrustedHttpsUrl('https://evil.example/x'), false);
    assert.equal(isTrustedHttpsUrl('not-a-url'), false);
    assert.equal(TRUSTED_HOSTS.includes('cdn.jsdelivr.net'), true);
  });
});
