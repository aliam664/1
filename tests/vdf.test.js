import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { extractSteamLibraries, parseVdf } from '../src/main/assetto-corsa/vdf.js';

describe('steam vdf', () => {
  it('parses libraryfolders.vdf', () => {
    const parsed = parseVdf(`
"libraryfolders"
{
  "0"
  {
    "path" "C:\\\\Program Files (x86)\\\\Steam"
  }
  "1"
  {
    "path" "D:\\\\SteamLibrary"
  }
}
`);
    const libs = extractSteamLibraries(parsed);
    assert.equal(libs.length, 2);
    assert.match(libs[0], /Steam/);
  });
});
