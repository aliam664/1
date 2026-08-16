import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { dictionaryKeys, loadLanguage, t } from '../src/renderer/i18n/i18n.js';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

function readJson(rel) {
  return JSON.parse(readFileSync(path.join(ROOT, rel), 'utf8'));
}

describe('i18n', () => {
  it('keeps fa and en key sets identical', () => {
    const fa = readJson('src/renderer/i18n/fa.json');
    const en = readJson('src/renderer/i18n/en.json');
    const faKeys = Object.keys(fa).sort();
    const enKeys = Object.keys(en).sort();
    assert.deepEqual(faKeys, enKeys);
    assert.equal(faKeys.length > 30, true);
  });

  it('has no empty strings in either locale', () => {
    for (const file of ['src/renderer/i18n/fa.json', 'src/renderer/i18n/en.json']) {
      const dict = readJson(file);
      for (const [key, value] of Object.entries(dict)) {
        assert.equal(typeof value, 'string', key);
        assert.equal(value.trim().length > 0, true, `${file} ${key}`);
      }
    }
  });

  it('resolves keys after switching language', () => {
    loadLanguage('fa');
    assert.equal(t('nav.home'), 'خانه');
    loadLanguage('en');
    assert.equal(t('nav.home'), 'Home');
    assert.deepEqual(dictionaryKeys('fa').sort(), dictionaryKeys('en').sort());
  });
});
