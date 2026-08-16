import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { validateCatalogDocument, validateCatalogItem } from '../src/main/catalog/validateItem.js';

const valid = {
  id: 'bmw-m3-e46',
  type: 'car',
  name: { fa: 'بی ام و', en: 'BMW M3 E46' },
  author: 'Community',
  description: { fa: 'توضیح', en: 'Desc' },
  version: '1.0.0',
  archiveType: 'rar',
  downloadUrl: 'https://github.com/aliam664/1/releases/download/x/a.rar',
  size: 10
};

describe('catalog validation', () => {
  it('accepts a well-formed item', () => {
    const result = validateCatalogItem(valid);
    assert.equal(result.ok, true);
  });

  it('rejects http URLs, bad types and duplicate ids', () => {
    assert.equal(validateCatalogItem({ ...valid, downloadUrl: 'http://x/a.rar' }).ok, false);
    assert.equal(validateCatalogItem({ ...valid, type: 'boat' }).ok, false);
    assert.equal(validateCatalogItem({ ...valid, archiveType: 'exe' }).ok, false);
    const doc = validateCatalogDocument({ items: [valid, valid] });
    assert.equal(doc.ok, false);
  });

  it('marks unpublished /demo/ packages as not packaged', () => {
    const result = validateCatalogItem({
      ...valid,
      downloadUrl: 'https://github.com/aliam664/1/releases/download/demo/a.rar'
    });
    assert.equal(result.ok, true);
    assert.equal(result.value.packaged, false);
  });
});
