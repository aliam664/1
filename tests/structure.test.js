import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { analyzeExtractedTree } from '../src/main/installer/structure.js';

describe('mod structure detector', () => {
  it('finds a nested content/cars root', () => {
    const result = analyzeExtractedTree(
      ['BMW_M3/content/cars/ks_bmw/data.acd', 'BMW_M3/content/cars/ks_bmw/ui/ui_car.json'],
      '/tmp/extract'
    );
    assert.equal(result.recognized, true);
    assert.equal(result.items[0].kind, 'car');
    assert.equal(result.items[0].folderName, 'ks_bmw');
    assert.equal(result.items[0].suggestedDest, 'content/cars/ks_bmw');
  });

  it('classifies a bare car folder by data.acd', () => {
    const result = analyzeExtractedTree(['my_car/data.acd', 'my_car/sfx/x.wav'], '/tmp/extract');
    assert.equal(result.items[0].kind, 'car');
    assert.equal(result.items[0].suggestedDest, 'content/cars/my_car');
  });

  it('classifies a track by models.ini', () => {
    const result = analyzeExtractedTree(['monza/models.ini', 'monza/ui/ui_track.json'], '/tmp/extract');
    assert.equal(result.items[0].kind, 'track');
    assert.equal(result.items[0].suggestedDest, 'content/tracks/monza');
  });

  it('returns unrecognized for random files', () => {
    const result = analyzeExtractedTree(['readme.pdf', 'photo.jpg'], '/tmp/extract');
    assert.equal(result.recognized, false);
  });
});
